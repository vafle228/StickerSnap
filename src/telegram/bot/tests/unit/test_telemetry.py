"""What the service tells OpenTelemetry: a span and a count per update, no token in a URL."""

from typing import Any

import pytest
import yarl
from aiogram.dispatcher.event.bases import UNHANDLED
from aiogram.types import TelegramObject, Update
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import InMemoryMetricReader, Sum
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter
from opentelemetry.trace import StatusCode

from stickersnap_bot.api.telegram import UpdateTelemetry
from stickersnap_bot.infrastructure.telemetry import hide_bot_token

UPDATE = Update.model_validate(
    {
        "update_id": 7,
        "message": {
            "message_id": 1,
            "date": 0,
            "chat": {"id": 42, "type": "private"},
            "text": "hi",
        },
    }
)


class Recorded:
    def __init__(self) -> None:
        self.spans = InMemorySpanExporter()
        tracer_provider = TracerProvider()
        tracer_provider.add_span_processor(SimpleSpanProcessor(self.spans))
        self.metrics = InMemoryMetricReader()
        meter_provider = MeterProvider(metric_readers=[self.metrics])
        self.middleware = UpdateTelemetry(
            tracer_provider.get_tracer("test"), meter_provider.get_meter("test")
        )

    def counts(self) -> dict[tuple[str, str], int]:
        data = self.metrics.get_metrics_data()
        assert data is not None
        found: dict[tuple[str, str], int] = {}
        for resource in data.resource_metrics:
            for scope in resource.scope_metrics:
                for metric in scope.metrics:
                    if metric.name != "stickersnap.bot.updates" or not isinstance(metric.data, Sum):
                        continue
                    for point in metric.data.data_points:
                        attributes = point.attributes or {}
                        key = (str(attributes["telegram.update.type"]), str(attributes["outcome"]))
                        found[key] = int(point.value)
        return found


async def test_an_update_gets_a_span_and_a_count() -> None:
    recorded = Recorded()

    async def handler(_: TelegramObject, __: dict[str, Any]) -> Any:
        return "answered"

    assert await recorded.middleware(handler, UPDATE, {}) == "answered"

    [span] = recorded.spans.get_finished_spans()
    assert span.name == "telegram message"
    assert span.attributes is not None
    assert span.attributes["telegram.update.id"] == 7
    assert span.attributes["telegram.update.outcome"] == "handled"
    assert recorded.counts() == {("message", "handled"): 1}


async def test_an_update_no_handler_took_counts_as_unhandled() -> None:
    recorded = Recorded()

    async def handler(_: TelegramObject, __: dict[str, Any]) -> Any:
        return UNHANDLED

    await recorded.middleware(handler, UPDATE, {})

    assert recorded.counts() == {("message", "unhandled"): 1}


async def test_a_failed_update_is_an_error_span() -> None:
    recorded = Recorded()

    async def handler(_: TelegramObject, __: dict[str, Any]) -> Any:
        raise RuntimeError("boom")

    with pytest.raises(RuntimeError):
        await recorded.middleware(handler, UPDATE, {})

    [span] = recorded.spans.get_finished_spans()
    assert span.status.status_code == StatusCode.ERROR
    assert recorded.counts() == {("message", "error"): 1}


@pytest.mark.parametrize(
    ("url", "kept"),
    [
        (
            "https://api.telegram.org/bot123:secret/sendMessage",
            "https://api.telegram.org/bot<hidden>/sendMessage",
        ),
        (
            "https://api.telegram.org/file/bot123:secret/videos/a.webm",
            "https://api.telegram.org/file/bot<hidden>/videos/a.webm",
        ),
    ],
)
def test_the_bot_token_never_reaches_a_span(url: str, kept: str) -> None:
    assert hide_bot_token(yarl.URL(url)) == kept

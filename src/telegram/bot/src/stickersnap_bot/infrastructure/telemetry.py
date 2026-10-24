"""Logs, traces and metrics of the service.

Logs always go to stdout (`docker logs`). With OTEL_EXPORTER_OTLP_ENDPOINT set, OpenTelemetry also
sends all three over OTLP/HTTP to the collector (Alloy on the VPS, docker/telegram/observability),
which passes them on to Loki, Tempo and Prometheus. Every log line written inside a span carries
its trace_id and span_id, in stdout and in Loki alike, so Grafana goes from a line to its trace and
back. Traced: the HTTP API (FastAPI), every Telegram update (api/telegram.py, UpdateTelemetry), the
calls to the Bot API (aiohttp, with the token cut out of the URL) and the SQL (SQLAlchemy).
"""

import logging
import re
import sys
from dataclasses import dataclass, field
from importlib.metadata import PackageNotFoundError, version

import yarl
from aiohttp import TraceRequestStartParams
from fastapi import FastAPI
from opentelemetry import metrics, trace
from opentelemetry._logs import set_logger_provider
from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.aiohttp_client import AioHttpClientInstrumentor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.logging import LoggingInstrumentor
from opentelemetry.instrumentation.sqlalchemy import SQLAlchemyInstrumentor
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import SERVICE_NAME, SERVICE_VERSION, Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.trace import Span
from sqlalchemy.ext.asyncio import AsyncEngine

SERVICE = "stickersnap-bot"
_PLAIN = "%(asctime)s %(levelname)s %(name)s: %(message)s"
# LoggingInstrumentor puts these on every record; "0" outside a span
_TRACED = _PLAIN + " trace_id=%(otelTraceID)s span_id=%(otelSpanID)s"
# the Bot API takes the token in the path: /bot<token>/sendMessage, /file/bot<token>/<path>
_BOT_TOKEN = re.compile(r"/bot[^/]+/")
_BOT_METHOD = re.compile(r"/bot[^/]+/(?P<method>[A-Za-z]+)$")


@dataclass
class Telemetry:
    """What start_telemetry turned on; shutdown() sends what is still buffered."""

    providers: list[TracerProvider | MeterProvider | LoggerProvider] = field(default_factory=list)

    def shutdown(self) -> None:
        for provider in self.providers:
            provider.shutdown()


def start_telemetry(endpoint: str | None, log_level: str = "INFO") -> Telemetry:
    """Logging to stdout and, with an OTLP endpoint, the export of logs, traces and metrics.
    Runs once, before the app is built: the Bot API's sessions are made traced from then on."""
    if endpoint is None:
        _log_to_stdout(_PLAIN, log_level)
        return Telemetry()

    base = endpoint.rstrip("/")
    resource = _resource()
    tracer_provider = TracerProvider(resource=resource)
    tracer_provider.add_span_processor(
        BatchSpanProcessor(OTLPSpanExporter(endpoint=f"{base}/v1/traces"))
    )
    trace.set_tracer_provider(tracer_provider)
    meter_provider = MeterProvider(
        resource=resource,
        metric_readers=[
            PeriodicExportingMetricReader(OTLPMetricExporter(endpoint=f"{base}/v1/metrics"))
        ],
    )
    metrics.set_meter_provider(meter_provider)
    logger_provider = LoggerProvider(resource=resource)
    logger_provider.add_log_record_processor(
        BatchLogRecordProcessor(OTLPLogExporter(endpoint=f"{base}/v1/logs"))
    )
    set_logger_provider(logger_provider)

    _log_to_stdout(_TRACED, log_level)
    # trace ids on every record, and a handler on the root logger that sends them to the collector
    LoggingInstrumentor().instrument(
        inject_trace_context=True,
        log_handler_level=logging.getLevelNamesMapping()[log_level],
    )
    AioHttpClientInstrumentor().instrument(url_filter=hide_bot_token, request_hook=_name_bot_call)
    return Telemetry([tracer_provider, meter_provider, logger_provider])


def instrument(app: FastAPI, engine: AsyncEngine) -> None:
    """Spans and metrics of the HTTP API and the SQL; Docker's health check is left out."""
    FastAPIInstrumentor.instrument_app(app, excluded_urls="health")
    SQLAlchemyInstrumentor().instrument(engine=engine.sync_engine)


def hide_bot_token(url: yarl.URL) -> str:
    """The Bot API's URL as a span keeps it: without the token."""
    return _BOT_TOKEN.sub("/bot<hidden>/", str(url), count=1)


def _name_bot_call(span: Span, params: TraceRequestStartParams) -> None:
    # "POST" for every Bot API call says nothing; "telegram sendMessage" does
    found = _BOT_METHOD.search(params.url.path)
    if found is not None and span.is_recording():
        span.update_name(f"telegram {found['method']}")


def _resource() -> Resource:
    try:
        app_version = version("stickersnap-bot")
    except PackageNotFoundError:
        app_version = "0.0.0"
    # OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES win over this default name
    resource = Resource.create({SERVICE_VERSION: app_version})
    if str(resource.attributes.get(SERVICE_NAME, "")).startswith("unknown_service"):
        resource = resource.merge(Resource({SERVICE_NAME: SERVICE}))
    return resource


def _log_to_stdout(format_: str, level: str) -> None:
    logging.basicConfig(
        stream=sys.stdout,
        level=level,
        format=format_,
        force=True,
    )
    for handler in logging.getLogger().handlers:
        handler.setFormatter(
            logging.Formatter(format_, defaults={"otelTraceID": "0", "otelSpanID": "0"})
        )
    # uvicorn's access log reaches the root logger (main runs it with log_config=None);
    # Docker's health check would fill it
    logging.getLogger("uvicorn.access").addFilter(_skip_health)


def _skip_health(record: logging.LogRecord) -> bool:
    args = record.args
    return not (isinstance(args, tuple) and len(args) >= 3 and args[2] == "/health")

"""The service serves exactly the operations of src/contract/bot-service.yaml, as B7 checks the
desktop's server against openapi.yaml. Bodies are checked against its schemas in
test_miniapp_api.py."""

from pathlib import Path

import yaml
from aiogram import Bot

from stickersnap_bot.api.app import Polling, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from tests.fakes import TOKEN, Tables, miniapp, sign_in

CONTRACT = Path(__file__).parents[5] / "src" / "contract" / "bot-service.yaml"
# the service's own routes, outside the contract
OUTSIDE = {"/health"}
METHODS = {"get", "post", "put", "patch", "delete"}


def operations(paths: dict[str, dict[str, dict[str, object]]]) -> set[tuple[str, str, object]]:
    return {
        (path, method, operation.get("operationId"))
        for path, item in paths.items()
        if path not in OUTSIDE
        for method, operation in item.items()
        if method in METHODS
    }


async def test_serves_exactly_the_contracts_operations() -> None:
    bot = Bot(TOKEN)
    served = create_app(
        bot, create_dispatcher(), Polling(), sign_in(Tables()), miniapp(Tables())
    ).openapi()
    await bot.session.close()
    contract = yaml.safe_load(CONTRACT.read_text(encoding="utf-8"))

    assert operations(served["paths"]) == operations(contract["paths"])

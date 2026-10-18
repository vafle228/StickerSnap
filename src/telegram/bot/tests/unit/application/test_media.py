from datetime import timedelta

from stickersnap_bot.application.media import MediaLinks
from tests.fakes import NOW

links = MediaLinks("123:secret")


def test_a_token_opens_to_its_file() -> None:
    opened = links.open(links.token("CAACAgIAAxk-_x", NOW), NOW)

    assert opened is not None
    assert opened[0] == "CAACAgIAAxk-_x"
    assert opened[1] - NOW > timedelta(hours=1)


def test_a_token_stays_the_same_within_its_period() -> None:
    # 09:00 and 09:59 share the hour, so the browser's cache keeps the image
    assert links.token("f", NOW) == links.token("f", NOW + timedelta(minutes=59))
    assert links.token("f", NOW) != links.token("f", NOW + timedelta(hours=1))


def test_an_expired_token_opens_to_nothing() -> None:
    token = links.token("f", NOW)

    assert links.open(token, NOW + timedelta(hours=2)) is None


def test_a_token_for_another_file_or_from_another_secret_opens_to_nothing() -> None:
    _, expires, signature = links.token("f", NOW).split(".")

    assert links.open(f"g.{expires}.{signature}", NOW) is None
    assert links.open(MediaLinks("other").token("f", NOW), NOW) is None
    assert links.open("nonsense", NOW) is None

"""Who is calling from Telegram: the Mini App now, the link confirmation (S4) and feedback (F1)
next. Every caller is remembered on the first visit, with the profile Telegram signed."""

from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.init_data import InitDataVerifier
from stickersnap_bot.application.persistence import UnitOfWorkFactory
from stickersnap_bot.domain.users import User


class SignIn:
    def __init__(
        self, unit_of_work: UnitOfWorkFactory, verifier: InitDataVerifier, clock: Clock
    ) -> None:
        self._unit_of_work = unit_of_work
        self._verifier = verifier
        self._clock = clock

    async def __call__(self, init_data: str) -> int:
        """The Telegram user id the launch data was signed for; UnauthorizedError otherwise."""
        now = self._clock()
        launch = self._verifier.verify(init_data, now)
        async with self._unit_of_work() as uow:
            known = await uow.users.get(launch.id)
            user = (
                User.first_seen(launch.id, launch.profile, now)
                if known is None
                else known.with_profile(launch.profile, now)
            )
            if user is not known:
                await uow.users.save(user)
                await uow.commit()
        return launch.id

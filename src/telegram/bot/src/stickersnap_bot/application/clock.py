from collections.abc import Callable
from datetime import datetime

# Aware UTC now; the composition root passes the real clock, tests a fixed one
type Clock = Callable[[], datetime]

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class Body(BaseModel):
    """A JSON body of the contract: camelCase on the wire, snake_case in Python."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)

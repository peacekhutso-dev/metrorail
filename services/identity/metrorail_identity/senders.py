"""Delivery of login codes. Local/test: logged (and returned as dev_code). Production: plug in
an SMS gateway (e.g. Clickatell, BulkSMS) and an email provider here."""

from metrorail_common.logging import get_logger

log = get_logger("senders")


class CodeSender:
    async def send(self, channel: str, target: str, code: str) -> None:
        log.info("login_code_dev_only", channel=channel, target=target[-4:], code=code)

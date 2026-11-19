"""Shared building blocks for every MetroRail service.

Every service is built the same way:

    from metrorail_common.app import create_app
    from metrorail_common.config import ServiceSettings

    settings = ServiceSettings(service_name="network")
    app = create_app(settings, routers=[...])

so logging, health checks, errors, auth, the database and events behave identically everywhere.
"""

__all__ = ["__version__"]
__version__ = "0.1.0"

-- One Postgres for local dev; each service gets its own database (no shared tables).
CREATE DATABASE network;
CREATE DATABASE tracking;
CREATE DATABASE identity;
CREATE DATABASE trips;
CREATE DATABASE notifications;
CREATE DATABASE ticketing;
CREATE DATABASE billing;
\c network
CREATE EXTENSION IF NOT EXISTS postgis;
\c tracking
CREATE EXTENSION IF NOT EXISTS postgis;

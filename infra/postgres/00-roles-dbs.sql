-- Roles y bases (solo desarrollo; claves de ejemplo NO productivas)
create role sses_owner login password 'sses_owner_dev';
create role sses_api   login password 'sses_api_dev';
create role keycloak   login password 'keycloak_dev';
create database sses     owner sses_owner;
create database keycloak owner keycloak;

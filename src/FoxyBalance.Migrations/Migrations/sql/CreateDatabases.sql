\set ON_ERROR_STOP on

-- Supply via -v or edit here
--\set dbname  'auntiedot'
--\set dbuser  'auntiedot_app'
--\set dbpass  'a-BAD_passw0rd'

-- 1) Create role if missing
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'dbuser', :'dbpass')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'dbuser')
\gexec

-- 2) Create database owned by that role
SELECT format('CREATE DATABASE %I OWNER %I', :'dbname', :'dbuser')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'dbname')
\gexec

-- 3) Connect into the new database
\connect :"dbname"

-- 4) Restrict connections
REVOKE CONNECT ON DATABASE :"dbname" FROM PUBLIC;
GRANT  CONNECT ON DATABASE :"dbname" TO :"dbuser";

-- 5) Transfer public schema ownership to the app role
ALTER SCHEMA public OWNER TO :"dbuser";

-- 6) Lock down schema permissions
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT  USAGE, CREATE ON SCHEMA public TO :"dbuser";

-- 7) Existing objects in schema
GRANT ALL PRIVILEGES ON ALL TABLES    IN SCHEMA public TO :"dbuser";
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO :"dbuser";
GRANT EXECUTE        ON ALL FUNCTIONS IN SCHEMA public TO :"dbuser";

-- 8) Default privileges for future objects
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT ALL     ON TABLES    TO :"dbuser";
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT ALL     ON SEQUENCES TO :"dbuser";
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO :"dbuser";

-- 9) Annotate the DB and role for clarity
COMMENT ON DATABASE :"dbname" IS 'Application database owned by :"dbuser"';
COMMENT ON ROLE     :"dbuser" IS 'Application login role with rights only on :"dbname"';

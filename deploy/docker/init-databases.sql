-- One database per service, on one server.
--
-- Separate databases, not separate schemas and certainly not shared tables:
-- the moment two services can join each other's tables, they are one service
-- with two deployment pipelines. A shared SERVER is a cost decision; a shared
-- DATABASE is an architecture decision, and a bad one here.
CREATE DATABASE strategyops_identity;
CREATE DATABASE strategyops_projects;
CREATE DATABASE strategyops_kpi;
CREATE DATABASE strategyops_risk;
CREATE DATABASE strategyops_issues;
CREATE DATABASE strategyops_benefits;
CREATE DATABASE strategyops_reporting;

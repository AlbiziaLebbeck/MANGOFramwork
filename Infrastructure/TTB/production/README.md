# Production candidate preparation

No Production resource has been provisioned or deployed. Follow `Documentation/TTB_UAT_AND_RELEASE_ACCEPTANCE.md`; current status is NO-GO.

`appsettings.Production.example.json` is a closed candidate, not a runnable profile. It deliberately contains no UAT Biller, bank issuer/key, database connection, session/game/collector token, approved cookie names or company approval. Provision actual Production values through the designated private store. Do not put secrets into this file or copy the UAT secret bundle. Start compatible inspection roles frozen; never use the template to claim a working bank integration.

Namespace `ttb-prod`, database `ttb_production` and Production private files/releases must be distinct from `ttb-uat`, `ttb-commerce-dev` and KKU. Runtime/owner/backup access and public exposure must be concretely reviewed before provisioning. The user's granted UAT-only backup approval does not authorize a Production backup role.

The deployment must enforce reader contract≥3 and all processes must support recovery freeze. A compatible binary rollback and a data restore have different procedures; financial Down guards are deliberate. No public ingress, DNS, shared ingress-controller change, credential grant or money command is performed by this directory.

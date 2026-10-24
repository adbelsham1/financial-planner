# DigitalOcean production bootstrap

The production application uses DigitalOcean's SYD1 region:

- App Platform app: `financial-planner-production`
- Managed PostgreSQL cluster: `financial-planner-production`
- Private Spaces bucket: `financial-planner-documents`

Create the PostgreSQL cluster and Spaces bucket through `doctl` in SYD1 before
creating the app. Keep generated credentials out of this repository.

Create the App Platform app from `.do/app.yaml`, then add the following
runtime secrets to both the API and Worker components:

- `ConnectionStrings__Postgres`
- `Infrastructure__WebOrigin` (API only)
- `Infrastructure__DocumentsBucket` (API only)
- `Infrastructure__SpacesEndpoint` (API only)
- `Infrastructure__SpacesAccessKey` (API only)
- `Infrastructure__SpacesSecretKey` (API only)

The API and Worker must be attached to the database private network. The
Spaces bucket must remain private; application code will use short-lived
signed URLs when document intake is implemented. The Web component builds with
`PUBLIC_API_BASE_URL=/api`, so it calls the API through the App Platform ingress
instead of depending on a generated application domain.

Set `DIGITALOCEAN_ACCESS_TOKEN` and `DIGITALOCEAN_APP_ID` as GitHub Actions
secrets. The deployment workflow then triggers `doctl apps create-deployment`
only after all checks pass on a `main` push.

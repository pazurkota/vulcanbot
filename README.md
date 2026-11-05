# 📚 vulcanbot

---

vulcanbot is a simple bot for fetching data about tests, homework, etc. from Vulcan into Calendar (Google Calendar, iCloud Calendar, Notion Calendar)


> [!WARNING]
> This is not VULCAN software. Using an unofficial client may violate the eduVULCAN terms of service. Use at your own risk.

## Requirements

- [Docker](https://docs.docker.com/get-docker/) with Docker Compose
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — only for local development and tests
- GNU Make — optional, every `make` target is a thin wrapper around `docker compose` / `dotnet`

## Configuration

Database credentials are read from a `.env` file in the repository root (it is git-ignored). Without it, everything defaults to `vulcanbot`:

```dotenv
POSTGRES_DB=vulcanbot
POSTGRES_USER=vulcanbot
POSTGRES_PASSWORD=change-me
```

Change the password if the bot runs anywhere other than your own machine — Postgres is exposed on port `5432` and stores the devices' private keys.

The bot reads its connection string from `ConnectionStrings:Default`. Docker Compose builds it from the variables above. When running locally, it falls back to `vulcanbot/appsettings.json` (`localhost:5432`, user/password `vulcanbot`). Override it with the `ConnectionStrings__Default` environment variable if needed.

## Quick start (Docker)

1. Start Postgres:

   ```bash
   make up            # docker compose up -d --build
   ```

2. Get registration tokens from eduVULCAN:
   1. Log in at <https://eduvulcan.pl> as usual (accept the consents if asked).
   2. In the same browser open <https://eduvulcan.pl/api/ap>. The page may look empty — the tokens are in a hidden `<input id="ap">`.
   3. Open the developer tools (**F12**) → **Console** and run:

      ```js
      copy(document.querySelector('#ap').value)
      ```

      (Chrome may ask you to type `allow pasting` first.) Alternatively, view the page source (**Ctrl+U**), search for `id="ap"` and copy the whole `<input>` element.
   4. Paste the copied text into `ap.json` in the repository root. It should look like:

      ```json
      {"Success":true,"ErrorMessage":null,"IsConsentAccepted":true,"Tokens":["eyJhbGciOi..."]}
      ```

3. Register the bot as a new device:

   ```bash
   make register      # docker compose run --rm --build -T vulcanbot register < ap.json
   ```

   This applies database migrations, registers a new RSA key pair with eduVULCAN and stores every student from the account in the `Accounts` table. Running it again for the same student replaces the device keys and keeps already synced data.

4. Delete `ap.json`. The tokens are one-time and short-lived — if registration fails, fetch new ones from `/api/ap`.

Your eduVULCAN password is never given to the bot. Pass `AP=path/to/file.json` to use a different file.

5. Fetch homework and exams into the database:

   ```bash
   make sync          # docker compose run --rm --build -T vulcanbot sync
   ```

   For every registered account this downloads homework and exams due from 2 weeks ago to 2 months ahead and stores them in the `Homeworks` and `Exams` tables. New items are added; items changed in Vulcan are updated and marked for the next calendar sync. Run it as often as you like — it is safe to repeat.

### Registration troubleshooting

| Problem | Fix |
|---|---|
| `document.querySelector('#ap')` returns `null` | You are not logged in or got redirected — log in again and reopen `/api/ap`. |
| `Consents are not accepted` | Accept the consents on eduvulcan.pl and copy the tokens again. |
| `No tokens` | The eduVULCAN account has no students linked. |
| `Hebe status: ...` | The server rejected the request. Tokens may have expired — copy fresh ones. If it keeps failing, the app version constants in `vulcanbot/Service/Hebe/HebeClient.cs` may need bumping. |

Pass the file through stdin as shown above, don't paste it into the terminal: long JSON can get truncated by the terminal's line length limit.

## Local development

```bash
make up            # Postgres (and the bot) in Docker
make build         # dotnet build
make run           # run the bot locally against localhost:5432
make test          # all tests; database tests start their own Postgres via Testcontainers (needs Docker)
make test-unit     # only tests that don't need Docker
```

To register from a local build instead of Docker (run it from `vulcanbot/`, where `appsettings.json` is):

```bash
cd vulcanbot && dotnet run -- register ../ap.json
```

### Database migrations

```bash
make migration NAME=AddSomething   # dotnet ef migrations add AddSomething --project vulcanbot
```

Requires the EF Core tool matching EF Core 10: `dotnet tool update -g dotnet-ef`. Migrations are applied automatically by `register`.

## Connecting to the database

```bash
make psql          # psql inside the Postgres container
```

Or connect any client (Rider Database tool, DBeaver, pgAdmin, `psql`) to:

| Setting | Value |
|---|---|
| Host | `localhost` (`postgres` from other Compose containers) |
| Port | `5432` |
| Database / user / password | values from `.env`, default `vulcanbot` |

Tables are created by EF Core with PascalCase names, so quote them in SQL:

```sql
SELECT "Symbol", "PupilId", "FirstName", "Surname", "ClassDisplay", "UnitName" FROM "Accounts";
```

The `PrivatePemKey` column holds the device's private key — anyone who has it can read the student's gradebook. Don't share query results that include it.

## Make targets

Run `make` to list all targets:

| Target | Description |
|---|---|
| `build`, `test`, `test-unit`, `run`, `clean` | .NET build, tests and local run |
| `migration NAME=...` | Add an EF Core migration |
| `up`, `down`, `logs`, `ps`, `image` | Docker Compose lifecycle |
| `psql` | Open psql in the Postgres container |
| `register [AP=ap.json]` | Register a device from the eduVULCAN `#ap` JSON |
| `sync` | Fetch homework and exams for all registered accounts |

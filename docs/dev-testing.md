# Local sample testing

Run `StartApp.cmd`, or run `./scripts/dev.ps1 start` from PowerShell. The client opens at http://127.0.0.1:5186 and the API listens on http://127.0.0.1:5086. The command file allows the local script to run for that PowerShell process only; it does not change the machine's execution-policy settings.

The launcher starts the API, Temporal worker and Vite client together. It reuses Docker containers `finyte-postgres` and `finyte-temporal`, database `finyte_recurring_samples`, and queue `finyte-recurring-samples`. Demo seeding is disabled. Existing imports and account settings persist. This is a local development-auth profile, bound to loopback, not a production deployment.

Commands:

```powershell
./scripts/dev.ps1 start
./scripts/dev.ps1 status
./scripts/dev.ps1 restart
./scripts/dev.ps1 stop
```

Starting an already-running instance does not duplicate it. Each start builds changed images using Docker's build cache, then runs `compose up -d`. Frontend dependencies are installed inside the client image. Vite watches mounted frontend source; rerun start after backend or dependency changes. Restart explicitly stops the application services first.

Docker manages the three detached services independently of the initiating Codex terminal. The launcher waits for API/client HTTP readiness and the worker polling message. Inspect logs using `docker compose -p finyte-sample-dev -f docker-compose.dev.yml logs --tail 50`. It never kills unrelated port listeners or replaces missing shared Docker infrastructure. Existing PostgreSQL and Temporal containers are required; a missing container stops startup for inspection rather than silently creating a new data volume.

Closing Codex or the launching terminal does not stop detached containers. Docker Desktop must stay running. Application containers use `restart: unless-stopped`; after reboot or stopping Docker Desktop, rerun start to check all dependencies. Stop leaves the database, shared infrastructure and volumes intact.

The personal `startapp` Codex skill uses this launcher. Ask for `/startapp` conversationally, or invoke `$startapp` when available in the skill picker.

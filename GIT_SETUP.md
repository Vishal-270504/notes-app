# Git Credentials & Repo Rename Setup

## Repo Renamed
Original clone: `https://github.com/Vishal-270504/notes-api-pagination.git`
Local folder renamed to: `notes-app`
Remote origin kept for reference. To push changes to a new repo named `notes-app`:

1. Create new GitHub repo named `notes-app`
2. Update remote URL:
```bash
cd notes-app
git remote set-url origin https://github.com/<YOUR_USERNAME>/notes-app.git
```

## Git Credentials Required
To push changes, you need GitHub credentials. Options:

### Option A: Personal Access Token (Recommended)
1. GitHub → Settings → Developer settings → Personal access tokens → Tokens classic
2. Generate token with `repo` scope
3. Use token as password:
```bash
git remote set-url origin https://<USERNAME>:<TOKEN>@github.com/<USERNAME>/notes-app.git
```

### Option B: Git Credential Manager
Windows already has credential.helper=manager configured.
First push will prompt for username/password or token.

### Current Local Git Config
```bash
git config user.name "Visha"
git config user.email "visha@example.com"
```

Set before commit:
```bash
git add .
git commit -m "Phase 2: Input validation with Zod and C#"
git push -u origin main
```

## What Was Changed
- Cloned `notes-api-pagination`
- Renamed folder to `notes-app`
- Phase 2 implementation: Input validation
- Backend: Title min 3 chars, description max 500
- Frontend: Zod validation
- README updated with learning documentation

No credentials are stored in repo. Use environment variables or Git Credential Manager for security.

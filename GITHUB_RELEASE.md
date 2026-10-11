# Natee — GitHub Publication & Release Guide (`GITHUB_RELEASE.md`)

This guide provides the exact commands required to review the working tree, initialize and commit the repository, connect to a GitHub remote, push the project, tag `v1.0.0`, and publish the first GitHub Release with compiled Windows and Linux build artifacts.

> **Security Note:** Never include personal access tokens, passwords, or SSH private keys in commands committed to files. Replace `<YOUR_GITHUB_USERNAME>` with your actual GitHub username.

---

## 1. Review the Working Tree Before Committing

Verify that no local SQLite databases (`*.db`), personal reports, build caches (`bin/`, `obj/`), or raw release archives (`artifacts/releases/*.tar.gz`, `*.zip`) are staged for commit:

```bash
cd /home/jhosmercastillo/Projects/Natee
git status
```

Ensure `.gitignore` is present and active.

---

## 2. Initialize Git Repository and Create the First Commit

If Git has not been initialized yet in `/home/jhosmercastillo/Projects/Natee`:

```bash
cd /home/jhosmercastillo/Projects/Natee

# Initialize repository with 'main' as the default branch
git init -b main

# Stage source code, tests, branding assets, CI workflow, and documentation
git add .

# Verify staged files (confirm no .db or binary archives are staged)
git status

# Create the initial commit
git commit -m "feat: initial release of Natee v1.0.0 MVP"
```

---

## 3. Connect to a GitHub Remote and Push

1. Create a new **empty** repository named `Natee` on GitHub (`https://github.com/new`) — do **not** initialize it with a README, license, or `.gitignore` on GitHub since they already exist locally.
2. Link your local repository and push the `main` branch:

```bash
# Using HTTPS (or use git@github.com:<YOUR_GITHUB_USERNAME>/Natee.git for SSH)
git remote add origin https://github.com/<YOUR_GITHUB_USERNAME>/Natee.git

git branch -M main
git push -u origin main
```

---

## 4. Create and Push the `v1.0.0` Version Tag

Create an annotated Git tag for the MVP release and push it to GitHub:

```bash
git tag -a v1.0.0 -m "Natee v1.0.0 — MVP Release (Calm monitoring. Clear diagnostics.)"
git push origin v1.0.0
```

---

## 5. Create the GitHub Release and Attach Compiled Assets

Verify that the compiled release archives exist in `artifacts/releases/`:
- `artifacts/releases/natee-v1.0.0-linux-x64.tar.gz` (Self-contained Linux x64 archive, verified on Fedora KDE Plasma)
- `artifacts/releases/natee-v1.0.0-win-x64.zip` (Self-contained Windows x64 archive)

### Option A: Using the GitHub CLI (`gh`)
If you have the GitHub CLI installed and authenticated (`gh auth login`), publish the release directly using [`RELEASE_NOTES_v1.0.0.md`](RELEASE_NOTES_v1.0.0.md):

```bash
gh release create v1.0.0 \
  artifacts/releases/natee-v1.0.0-linux-x64.tar.gz \
  artifacts/releases/natee-v1.0.0-win-x64.zip \
  --title "Natee v1.0.0 — MVP Release" \
  --notes-file RELEASE_NOTES_v1.0.0.md
```

### Option B: Using the GitHub Web Interface
1. Open `https://github.com/<YOUR_GITHUB_USERNAME>/Natee/releases/new`.
2. Select tag **`v1.0.0`**.
3. Set Release Title to **`Natee v1.0.0 — MVP Release`**.
4. Drag and drop both files from `artifacts/releases/`:
   - `natee-v1.0.0-linux-x64.tar.gz`
   - `natee-v1.0.0-win-x64.zip`
5. Copy the contents of [`RELEASE_NOTES_v1.0.0.md`](RELEASE_NOTES_v1.0.0.md) into the release description box and click **Publish release**.

# Releasing GlueyKeys

GlueyKeys uses `dev` for day-to-day work and `main` for release-ready code.

## Normal Flow

1. Work on `dev`.
2. Merge `dev` into `main` when the app is ready to release.
3. In GitHub, open **Actions**.
4. Run **Prepare Release** on the `main` branch.
5. Keep `bump` as `patch` for the usual `0.0.x + 1` release.
6. Add release notes and run the workflow.

The workflow will:

- update app version files,
- update `web/index.html` download links,
- commit the release changes to `main`,
- create and push the `vX.Y.Z` tag,
- build the self-contained single-file `GlueyKeys.exe`,
- create the GitHub release and upload the executable.

Vercel should redeploy the website from the updated `main` branch.

## Publishing An Existing Tag

If a tag already exists but the release asset needs to be rebuilt or re-uploaded, run **Publish Release** manually and provide the tag, for example:

```text
v0.0.4
```

The source at that tag must already contain matching version files and website links.

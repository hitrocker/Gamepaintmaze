# Paint Maze legal site publishing

This directory is published as the Paint Maze GitHub Pages legal site.

## Published URLs

- `https://hitrocker.github.io/paintmaze/privacy.html`
- `https://hitrocker.github.io/paintmaze/account-deletion.html`

Put the first URL in `LegalConfig.OnlinePrivacyPolicyUrl` in `Assets/Scripts/Menu/PrivacyPolicyPanel.cs`, the privacy-policy placeholders, and Google Play. Put the second URL in the account-deletion placeholders and Play Console.

## Repository setup

1. Copy the contents of `legal-site/` into an empty local folder named `paintmaze`.
2. Review the pages in a browser.
3. In that new folder, run:

   ```sh
   git init
   git add .
   git commit -m "Publish Paint Maze privacy pages"
   gh repo create paintmaze --public --source=. --remote=origin --push
   ```

4. On GitHub, open **paintmaze → Settings → Pages**.
5. Under **Build and deployment**, choose **Deploy from a branch**.
6. Select branch **main**, folder **/(root)**, then **Save**.
7. Wait for the Pages deployment to finish and open both HTTPS URLs above.
8. Confirm the pages work without signing in and on a mobile browser.

If Git uses `master` as the initial branch, rename it before creating the repository:

```sh
git branch -M main
```

## Release follow-up

- Replace `LegalConfig.OnlinePrivacyPolicyUrl` and rebuild the app so **Open Online Policy** becomes visible.
- Enter the privacy URL in the Play Console store listing and Data Safety form.
- Enter the deletion URL in the Play Console account deletion field.
- Deploy the updated Firestore rules.
- Keep the published pages available for as long as the app or retained user data exists.

# Google Play Data Safety Checklist

This checklist describes the July 14, 2026 implementation. Re-check the final Android App Bundle and every enabled Firebase Console product immediately before submission. Google Play definitions and Firebase behavior can change.

## Initial declarations

- **Does the app collect or share required user data types?** Yes—data is transmitted from the device to Firebase.
- **Is all collected user data encrypted in transit?** Yes, Firebase SDK traffic uses HTTPS/TLS.
- **Can users request data deletion?** Yes—in-app through **Account → Delete Account**, and through the public deletion-request page.
- **Public privacy policy URL:** `https://hitrocker.github.io/paintmaze/privacy.html`
- **Account deletion URL:** `https://hitrocker.github.io/paintmaze/account-deletion.html`

## Personal info

### Name

- **Collected:** Yes. The player may provide a public display name or alias.
- **Shared:** Do not mark as third-party sharing solely for Firebase acting as a service provider. The name is intentionally displayed to authenticated leaderboard users; verify whether the current Play Console wording treats this user-initiated public disclosure as exempt.
- **Purpose:** App functionality and account management.
- **Required or optional:** Optional.
- **Ephemeral:** No.

### Email address

- **Collected:** Yes, only when the player creates or uses an email/password account or Google supplies an email to Firebase Authentication.
- **Shared:** No under the service-provider exception; verify the final provider configuration.
- **Purpose:** App functionality and account management.
- **Required or optional:** Optional because guest play is supported.
- **Ephemeral:** No.

### User IDs

- **Collected:** Yes. Firebase creates a UID for anonymous and registered accounts.
- **Shared:** No under the service-provider exception.
- **Purpose:** App functionality, account management, fraud prevention/security.
- **Required or optional:** Required by the current Home-first Firebase session design.
- **Ephemeral:** No.

Do not select address, phone number, race/ethnicity, political or religious beliefs, sexual orientation, or other personal-info categories unless future features add them.

## App activity

### Other user-generated content / gameplay progress

- **Collected:** Yes. Public display name, highest completed level per difficulty, and update/reached timestamps are stored in Cloud Firestore.
- **Shared:** Treat the leaderboard display as user-visible disclosure. Verify the exact Play Console exemption language during submission; if uncertain, disclose sharing conservatively.
- **Purpose:** App functionality.
- **Required or optional:** Progress upload occurs automatically under the current architecture.
- **Ephemeral:** No.

### App interactions

- **Collected:** Potentially yes through bundled Firebase Analytics components, even though the game emits no custom analytics events.
- **Shared:** No under the service-provider exception, assuming no advertising or external analytics destinations are configured.
- **Purpose:** Analytics and app functionality.
- **Required or optional:** Automatic if Firebase Analytics collection is enabled in the final build.
- **Ephemeral:** No.

## Device or other IDs

- **Collected:** Conservatively select Yes if Firebase Analytics is enabled in the release build, because app-instance or device-related identifiers may be processed.
- **Shared:** No under the service-provider exception.
- **Purpose:** Analytics, fraud prevention/security, and app functionality as applicable.
- **Required or optional:** Automatic if enabled.
- **Ephemeral:** No.

## Diagnostics

- Do not select **Crash logs** unless Crashlytics or another crash service is added.
- Consider selecting **Diagnostics** if the final Firebase Analytics/App Check/service configuration transmits diagnostic information. Confirm using the final SDK list and Firebase documentation.

## Data types currently not requested

Unless the implementation changes, do not select:

- precise or approximate location requested by the app;
- contacts;
- photos or videos;
- audio files or voice recordings;
- files and documents;
- calendar;
- health and fitness;
- financial or payment information;
- messages;
- web browsing history; or
- installed-app inventory.

Firebase or Google may derive coarse location from IP for security/service operation. Re-check Google's latest disclosures to determine whether the current Play form requires a location declaration for the final configuration.

## Security practices

- Data is encrypted in transit.
- Firestore rules limit leaderboard writes and deletion to the authenticated owner and allowed board IDs.
- Leaderboard level updates are monotonic.
- Account deletion requires recent provider verification for email and Google accounts.
- Guest accounts can also be deleted.

Do not claim independent security review, data deletion certification, or Families Policy compliance unless completed separately.

## Release blockers

1. Confirm both GitHub Pages URLs remain publicly accessible.
2. Put the public privacy URL in the Play Console store listing and Data Safety form.
3. Put the public account-deletion URL in the Play Console account-deletion field.
4. Deploy the updated `firestore.rules` that allow owner deletion.
5. Confirm the final AAB's Firebase modules and whether Analytics collection is enabled.
6. Complete the Data Safety form using the final Play Console wording.
7. Keep screenshots or exported answers as release evidence.

## Deploy the updated Firestore policy

From the Paint Maze project root, authenticate the Firebase CLI and select the same Firebase project used by `google-services.json`:

```sh
firebase login
firebase use --add
firebase deploy --only firestore:rules,firestore:indexes
```

After deployment, confirm in Firebase Console that the active rule contains:

```text
allow delete: if owns(userId) && playableBoard(boardId);
```

Run the local rules suite before deployment with:

```sh
cd firestore-tests
npm install
JAVA_HOME="/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home" \
  PATH="/opt/homebrew/opt/openjdk@21/bin:$PATH" npm test
```

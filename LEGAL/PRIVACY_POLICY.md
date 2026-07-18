# Paint Maze Privacy Policy

**Effective date:** July 14, 2026  
**Publisher:** `hitrocker`  
**Privacy contact:** `hitrocker27@gmail.com`

**Published policy:** https://hitrocker.github.io/paintmaze/privacy.html

## Overview

Paint Maze is a mobile puzzle game. The game can be played immediately with an automatically created guest account. Players may optionally connect or sign in with Google or email/password to restore progress across installations and participate under a chosen display name.

## Data we process

### Account and identity data

- Firebase user ID.
- Authentication provider (anonymous, Google, or email/password).
- Email address when an email/password account is created, and account information supplied by Google when Google sign-in is used.
- Optional public display name (3–16 supported characters).

Passwords are handled by Firebase Authentication. Paint Maze does not store readable passwords.

### Game and leaderboard data

- Highest completed level for each difficulty.
- Public display name.
- Timestamps for when leaderboard progress was reached or updated.

Leaderboard display names and progress are visible to authenticated Paint Maze players. Email addresses are not placed in leaderboard records.

### Device-local data

The app stores the selected difficulty, per-account progress cache, theme, sound, haptics, camera shake, and paint-splash preferences on the device. Generated-level cache files may also be stored locally. This data normally remains until it is reset, deleted through the applicable account flow, or the app is uninstalled.

### Analytics and service data

The app bundle currently includes Firebase Analytics dependencies. Even though Paint Maze does not currently emit custom analytics events, enabled Firebase services may process app-instance identifiers, device/app information, approximate diagnostics, and service interaction data according to the Firebase configuration and Google's documentation.

## Why we process data

We process data to:

- create and authenticate guest or registered accounts;
- restore and merge game progress;
- operate global progress leaderboards;
- keep the app secure, reliable, and functional;
- remember device preferences; and
- comply with legal obligations.

## Legal bases

Where applicable, processing is necessary to provide the game and account features requested by the player, for legitimate interests in operating and securing the service, or based on consent where a platform or law requires it.

## Service providers

Paint Maze uses Google Firebase, including Firebase Authentication, Cloud Firestore, and bundled Firebase Analytics components. Google may process data on the publisher's behalf under its Firebase terms and privacy documentation:

- https://firebase.google.com/support/privacy
- https://policies.google.com/privacy

Google sign-in also uses Android Credential Manager and Google identity services.

## Sharing and sale

We do not sell personal data. Data is shared with service providers only as needed to operate the features described above, or when required by law. A player's chosen display name and leaderboard progress are intentionally shared with other authenticated players.

## Retention

- Firebase Authentication account data is retained while the account exists.
- Leaderboard records are retained while the account exists unless removed earlier.
- Account deletion removes the current user's four leaderboard records and then deletes the Firebase Authentication account.
- Device-local preferences remain after account deletion because they are device settings rather than account data. The deleted user's local progress is removed.
- Service providers may retain limited logs or backups under their own documented retention schedules and legal obligations.

## Account and data deletion

In the app, open **Account → Delete Account**, review both confirmation steps, and complete provider reauthentication when requested. The app deletes the signed-in user's leaderboard records, Firebase Authentication account, and that user's local progress, then starts a fresh guest session.

An external deletion-request page is provided at:

https://hitrocker.github.io/paintmaze/account-deletion.html

If the app cannot be accessed, contact `hitrocker27@gmail.com`. Verification may be required to prevent unauthorized deletion.

## Security

We use Firebase authentication, owner-scoped Firestore security rules, encrypted HTTPS transport provided by Firebase, and provider reauthentication for sensitive deletion actions. No system can guarantee absolute security.

## Children's privacy

Paint Maze is not directed to children under 13 (or the minimum age required in the user's country) and does not knowingly seek personal data from such children. If you believe a child provided personal data contrary to applicable law, contact us for review and deletion.

## Data not intentionally collected

Paint Maze does not intentionally request precise location, contacts, photos, videos, audio recordings, health information, payment-card details, SMS, or call logs.

## International processing

Firebase and Google may process information in countries other than the player's country. Applicable contractual and legal safeguards are described in Google's privacy and data-processing documentation.

## Your choices and rights

Players may use a guest account, choose whether to connect Google or email, change their public display name, sign out, or delete their account. Depending on location, users may also have rights to access, correct, object to, restrict, or request deletion of personal data. Contact the privacy address above.

## Changes

This policy may be updated when app features, providers, or laws change. The effective date will be updated and material changes will be communicated as required.

## Contact

`hitrocker`  
`hitrocker27@gmail.com`

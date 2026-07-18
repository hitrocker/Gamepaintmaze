# Paint Maze Account Deletion

**Publisher:** `hitrocker`  
**Support:** `hitrocker27@gmail.com`

## Delete from the app

1. Open Paint Maze.
2. Tap the account icon on the Home screen.
3. Tap **Delete Account**.
4. Read the first warning and tap **Continue**.
5. Complete the final confirmation:
   - email accounts enter the current password;
   - Google accounts verify using Google sign-in;
   - guest accounts require no provider reauthentication.
6. Tap **Delete Forever**.

The app first pauses synchronization, then removes the account owner's records from all four leaderboards, deletes the Firebase Authentication account, clears that user's local progress, and creates a new guest session.

## Data deleted

- Firebase Authentication account and Firebase user ID.
- Email association held by Firebase Authentication, where applicable.
- Public display name associated with the Firebase account.
- Highest completed level and timestamps on the Easy, Medium, Hard, and Extra Hard leaderboards.
- Deleted account's progress cache on the current device.

## Data retained

Device-wide settings—including theme, sound, haptics, camera shake, and paint-splash preferences—are retained. Firebase or Google may retain limited security logs, backups, or legally required records under their published retention terms.

## Delete without access to the app

Send a request to `hitrocker27@gmail.com` with the subject **Paint Maze account deletion request**. Include the sign-in method and enough non-sensitive information to locate and verify the account. Never send a password or Google token by email.

Requests will be handled within the period required by applicable law. Verification may be required to protect accounts from unauthorized deletion.

## Google Play setup

Publish this page at a stable public HTTPS URL and enter that URL in the Play Console account-deletion field:

https://hitrocker.github.io/paintmaze/account-deletion.html

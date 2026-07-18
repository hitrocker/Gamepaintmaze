package com.hitrocker.paintmaze.auth;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.core.content.ContextCompat;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.unity3d.player.UnityPlayer;

public final class GoogleCredentialBridge {
    private GoogleCredentialBridge() {
    }

    public static void signIn(
            String gameObject,
            String successMethod,
            String errorMethod,
            String webClientId) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            send(gameObject, errorMethod, "Android activity is unavailable.");
            return;
        }

        activity.runOnUiThread(() -> {
            try {
                com.google.android.libraries.identity.googleid.GetGoogleIdOption googleOption =
                        new com.google.android.libraries.identity.googleid.GetGoogleIdOption.Builder()
                                .setFilterByAuthorizedAccounts(false)
                                .setServerClientId(webClientId)
                                .setAutoSelectEnabled(false)
                                .build();

                GetCredentialRequest request = new GetCredentialRequest.Builder()
                        .addCredentialOption(googleOption)
                        .build();

                CredentialManager manager = CredentialManager.create(activity);
                manager.getCredentialAsync(
                        activity,
                        request,
                        new CancellationSignal(),
                        ContextCompat.getMainExecutor(activity),
                        new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                            @Override
                            public void onResult(GetCredentialResponse result) {
                                Credential credential = result.getCredential();
                                if (!(credential instanceof CustomCredential)
                                        || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL
                                        .equals(credential.getType())) {
                                    send(gameObject, errorMethod,
                                            "Google returned an unsupported credential.");
                                    return;
                                }

                                try {
                                    GoogleIdTokenCredential googleCredential =
                                            GoogleIdTokenCredential.createFrom(
                                                    ((CustomCredential) credential).getData());
                                    send(gameObject, successMethod, googleCredential.getIdToken());
                                } catch (Exception exception) {
                                    send(gameObject, errorMethod,
                                            "Google identity token could not be read.");
                                }
                            }

                            @Override
                            public void onError(GetCredentialException exception) {
                                String message = exception.getMessage();
                                send(gameObject, errorMethod,
                                        message == null || message.isEmpty()
                                                ? "Google Sign-In was canceled."
                                                : message);
                            }
                        });
            } catch (Exception exception) {
                String message = exception.getMessage();
                send(gameObject, errorMethod,
                        message == null || message.isEmpty()
                                ? "Could not start Google Sign-In."
                                : message);
            }
        });
    }

    private static void send(String gameObject, String method, String value) {
        UnityPlayer.UnitySendMessage(gameObject, method, value == null ? "" : value);
    }
}

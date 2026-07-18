#!/usr/bin/env python3
import argparse
import json
import random
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


PROJECT_ID = "hitrocker-paintmaze"
API_KEY = "AIzaSyATFj18hqTqHkLll6ikLDHlTE-g39LSTto"
ROOT = Path(__file__).resolve().parents[1]
STATE_PATH = ROOT / "Library" / "PaintMazeLeaderboardSeed" / "seed-users.json"

NAMES = [
    "MazeMia",
    "TileTamer",
    "ColorDash",
    "RollingNova",
    "PaintPilot",
    "SwiftSphere",
    "PuzzlePanda",
    "RouteMaster",
    "NeonTrail",
    "CleverCube",
    "LunaRoll",
    "SplashFox",
    "MazeAce",
    "TileWizard",
    "InkRunner",
    "GlideGuru",
    "PixelPath",
    "BrightBall",
    "QuietQuest",
    "PathFinder",
    "RollLogic",
    "MazeBloom",
    "ColorComet",
    "TileScout",
    "PaintPulse",
]

LEVELS = {
    "easy": [
        195, 187, 179, 171, 163, 155, 147, 139, 131, 123,
        115, 107, 99, 91, 83, 75, 67, 59, 51, 43,
        37, 32, 28, 24, 20,
    ],
    "medium": [
        164, 157, 150, 143, 136, 129, 122, 115, 108, 101,
        94, 87, 80, 73, 66, 59, 52, 45, 38, 32,
        27, 23, 19, 16, 13,
    ],
    "hard": [
        128, 122, 116, 110, 104, 98, 92, 86, 80, 74,
        68, 62, 56, 50, 44, 39, 34, 30, 26, 22,
        19, 16, 13, 10, 7,
    ],
    "extraHard": [
        96, 91, 86, 81, 76, 71, 66, 61, 56, 51,
        46, 41, 37, 33, 29, 25, 22, 19, 16, 13,
        11, 9, 7, 5, 3,
    ],
}


def request_json(url, payload=None, token=None, method="POST", form=False):
    headers = {}
    data = None
    if payload is not None:
        if form:
            data = urllib.parse.urlencode(payload).encode("utf-8")
            headers["Content-Type"] = "application/x-www-form-urlencoded"
        else:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"

    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            body = response.read()
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {url} failed: {error.code} {body}") from error


def sign_up_anonymous():
    return request_json(
        f"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={API_KEY}",
        {"returnSecureToken": True},
    )


def refresh_id_token(refresh_token):
    result = request_json(
        f"https://securetoken.googleapis.com/v1/token?key={API_KEY}",
        {"grant_type": "refresh_token", "refresh_token": refresh_token},
        form=True,
    )
    return result["id_token"], result["refresh_token"]


def document_name(board, uid):
    return (
        f"projects/{PROJECT_ID}/databases/(default)/documents/"
        f"leaderboards/{board}/entries/{uid}"
    )


def write_entry(token, uid, board, name, level):
    request_json(
        f"https://firestore.googleapis.com/v1/projects/{PROJECT_ID}"
        "/databases/(default)/documents:commit",
        {
            "writes": [
                {
                    "update": {
                        "name": document_name(board, uid),
                        "fields": {
                            "displayName": {"stringValue": name},
                            "highestLevel": {"integerValue": str(level)},
                        },
                    },
                    "updateTransforms": [
                        {
                            "fieldPath": "reachedAt",
                            "setToServerValue": "REQUEST_TIME",
                        },
                        {
                            "fieldPath": "updatedAt",
                            "setToServerValue": "REQUEST_TIME",
                        },
                    ],
                }
            ]
        },
        token=token,
    )


def delete_entry(token, uid, board):
    url = (
        f"https://firestore.googleapis.com/v1/{document_name(board, uid)}"
    )
    try:
        request_json(url, token=token, method="DELETE")
    except RuntimeError as error:
        if " 404 " not in str(error):
            raise


def delete_account(token):
    request_json(
        f"https://identitytoolkit.googleapis.com/v1/accounts:delete?key={API_KEY}",
        {"idToken": token},
    )


def save_state(state):
    STATE_PATH.parent.mkdir(parents=True, exist_ok=True)
    STATE_PATH.write_text(json.dumps(state, indent=2), encoding="utf-8")


def load_state():
    if not STATE_PATH.exists():
        return {"projectId": PROJECT_ID, "users": []}
    return json.loads(STATE_PATH.read_text(encoding="utf-8"))


def assignments():
    result = {name: {} for name in NAMES}
    for board, levels in LEVELS.items():
        shuffled = NAMES.copy()
        random.Random(f"paintmaze-{board}-2026").shuffle(shuffled)
        for name, level in zip(shuffled, levels):
            result[name][board] = level
    return result


def seed():
    state = load_state()
    if state["users"]:
        raise RuntimeError(
            f"Seed state already exists at {STATE_PATH}. "
            "Run with --cleanup before seeding again."
        )

    planned = assignments()
    for index, name in enumerate(NAMES, start=1):
        auth = sign_up_anonymous()
        user = {
            "uid": auth["localId"],
            "name": name,
            "refreshToken": auth["refreshToken"],
            "boards": [],
        }
        state["users"].append(user)
        save_state(state)

        for board in LEVELS:
            write_entry(
                auth["idToken"],
                user["uid"],
                board,
                name,
                planned[name][board],
            )
            user["boards"].append(board)
            save_state(state)

        print(f"[{index:02d}/{len(NAMES)}] Added {name}")
        time.sleep(0.08)

    print(
        f"Seeded {len(NAMES)} users and "
        f"{len(NAMES) * len(LEVELS)} leaderboard entries."
    )
    print(f"Cleanup state saved at {STATE_PATH}")


def cleanup():
    state = load_state()
    users = state.get("users", [])
    if not users:
        print("No temporary leaderboard users found.")
        return

    remaining = users.copy()
    for index, user in enumerate(users, start=1):
        token, new_refresh_token = refresh_id_token(user["refreshToken"])
        user["refreshToken"] = new_refresh_token
        save_state(state)

        for board in user.get("boards", LEVELS.keys()):
            delete_entry(token, user["uid"], board)
        delete_account(token)

        remaining.remove(user)
        state["users"] = remaining
        save_state(state)
        print(f"[{index:02d}/{len(users)}] Removed {user['name']}")

    STATE_PATH.unlink(missing_ok=True)
    print("Removed all temporary leaderboard entries and accounts.")


def status():
    state = load_state()
    users = state.get("users", [])
    entry_count = sum(len(user.get("boards", [])) for user in users)
    print(f"Tracked temporary users: {len(users)}")
    print(f"Tracked leaderboard entries: {entry_count}")
    print(f"State file: {STATE_PATH}")


def verify():
    state = load_state()
    users = state.get("users", [])
    if not users:
        raise RuntimeError("No temporary leaderboard users are tracked.")

    token, _ = refresh_id_token(users[0]["refreshToken"])
    temporary_ids = {user["uid"] for user in users}
    for board in LEVELS:
        result = request_json(
            f"https://firestore.googleapis.com/v1/projects/{PROJECT_ID}"
            f"/databases/(default)/documents/leaderboards/{board}/entries"
            "?pageSize=100",
            token=token,
            method="GET",
        )
        documents = result.get("documents", [])
        temporary = [
            document for document in documents
            if document["name"].rsplit("/", 1)[-1] in temporary_ids
        ]
        levels = {
            int(document["fields"]["highestLevel"]["integerValue"])
            for document in temporary
        }
        print(
            f"{board}: {len(temporary)} temporary entries, "
            f"{len(levels)} distinct levels, {len(documents)} total entries"
        )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cleanup", action="store_true")
    parser.add_argument("--status", action="store_true")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    if args.cleanup:
        cleanup()
    elif args.verify:
        verify()
    elif args.status:
        status()
    else:
        seed()


if __name__ == "__main__":
    main()

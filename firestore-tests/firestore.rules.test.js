const fs = require("node:fs");
const path = require("node:path");
const {
  initializeTestEnvironment,
  assertFails,
  assertSucceeds,
} = require("@firebase/rules-unit-testing");
const { deleteDoc, doc, setDoc } = require("firebase/firestore");

describe("Paint Maze Firestore owner deletion", () => {
  let testEnv;
  const projectId = "paintmaze-rules-test";

  before(async () => {
    const rules = fs.readFileSync(
      path.resolve(__dirname, "../firestore.rules"),
      "utf8",
    );
    testEnv = await initializeTestEnvironment({
      projectId,
      firestore: { rules },
    });
  });

  after(async () => {
    await testEnv.cleanup();
  });

  beforeEach(async () => {
    await testEnv.clearFirestore();
  });

  async function seed(documentPath) {
    await testEnv.withSecurityRulesDisabled(async (context) => {
      await setDoc(doc(context.firestore(), documentPath), {
        highestLevel: 8,
        displayName: "Maze Player",
        reachedAt: new Date(),
        updatedAt: new Date(),
      });
    });
  }

  it("allows an authenticated owner to delete an allowed board entry", async () => {
    const entry = "leaderboards/easy/entries/owner-user";
    await seed(entry);
    const db = testEnv.authenticatedContext("owner-user").firestore();
    await assertSucceeds(deleteDoc(doc(db, entry)));
  });

  it("denies unauthenticated deletion", async () => {
    const entry = "leaderboards/medium/entries/owner-user";
    await seed(entry);
    const db = testEnv.unauthenticatedContext().firestore();
    await assertFails(deleteDoc(doc(db, entry)));
  });

  it("denies deletion of another user's entry", async () => {
    const entry = "leaderboards/hard/entries/owner-user";
    await seed(entry);
    const db = testEnv.authenticatedContext("different-user").firestore();
    await assertFails(deleteDoc(doc(db, entry)));
  });

  it("denies owner deletion on an unknown board", async () => {
    const entry = "leaderboards/not-a-board/entries/owner-user";
    await seed(entry);
    const db = testEnv.authenticatedContext("owner-user").firestore();
    await assertFails(deleteDoc(doc(db, entry)));
  });
});

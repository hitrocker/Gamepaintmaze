namespace PaintMaze.Menu
{
    public enum UiPage
    {
        Home,
        Game,
        Complete,
        SignIn,
        Account,
        Leaderboard
    }

    public enum AccountEntryDestination
    {
        SignIn,
        Account
    }

    public static class UiFlowPolicy
    {
        public static bool IsVisible(UiPage active, UiPage candidate) =>
            active == candidate;

        public static AccountEntryDestination AccountDestination(
            bool hasUser, bool isAnonymous) =>
            !hasUser || isAnonymous
                ? AccountEntryDestination.SignIn
                : AccountEntryDestination.Account;

        public static bool UsesExistingAccount(bool returningMode) => returningMode;
    }
}

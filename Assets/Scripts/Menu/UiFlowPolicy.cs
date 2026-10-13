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

    public enum SystemBackAction
    {
        None,
        DismissHomeOverlay,
        ReturnHome
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

        public static SystemBackAction BackAction(
            UiPage active,
            bool hasHomeOverlay)
        {
            if (active == UiPage.Home && hasHomeOverlay)
                return SystemBackAction.DismissHomeOverlay;
            if (active == UiPage.Leaderboard)
                return SystemBackAction.ReturnHome;
            return SystemBackAction.None;
        }
    }
}

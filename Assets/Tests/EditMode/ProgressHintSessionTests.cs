using NUnit.Framework;
using PaintMaze.Game;

namespace PaintMaze.Tests
{
    public class ProgressHintSessionTests
    {
        [Test]
        public void PaintedOnlyMove_RefreshesWhenBallRests()
        {
            var session = new ProgressHintSession();
            session.Begin();
            session.ObserveTile(false);

            Assert.IsTrue(session.ShouldRefreshAtRest(false));
            Assert.IsFalse(session.ShouldEndAtRest(false));
        }

        [Test]
        public void DifferentValidPaintedMove_StillRequestsRecalculation()
        {
            var session = new ProgressHintSession();
            session.Begin();
            session.ObserveTile(false);
            session.ObserveTile(false);

            Assert.IsTrue(session.Active);
            Assert.IsTrue(session.ShouldRefreshAtRest(false));
        }

        [Test]
        public void InvalidSwipe_LeavesCurrentSessionUnchanged()
        {
            var session = new ProgressHintSession();
            session.Begin();

            Assert.IsTrue(session.Active);
            Assert.IsTrue(session.ShouldRefreshAtRest(false));
        }

        [Test]
        public void FirstNewTile_EndsGuidanceAtRest()
        {
            var session = new ProgressHintSession();
            session.Begin();
            session.ObserveTile(false);
            session.ObserveTile(true);

            Assert.IsTrue(session.FoundNewTile);
            Assert.IsTrue(session.ShouldEndAtRest(false));
            Assert.IsFalse(session.ShouldRefreshAtRest(false));
        }

        [Test]
        public void BufferedPaintedChain_RemainsActiveUntilRestHandling()
        {
            var session = new ProgressHintSession();
            session.Begin();
            session.ObserveTile(false);

            Assert.IsTrue(session.Active);
            Assert.IsFalse(session.FoundNewTile);

            session.ObserveTile(true);

            Assert.IsTrue(session.Active);
            Assert.IsTrue(session.ShouldEndAtRest(false));
        }

        [Test]
        public void CompletionAndResetClearTheSession()
        {
            var session = new ProgressHintSession();
            session.Begin();

            Assert.IsTrue(session.ShouldEndAtRest(true));

            session.End();

            Assert.IsFalse(session.Active);
            Assert.IsFalse(session.FoundNewTile);
            Assert.IsFalse(session.ShouldRefreshAtRest(false));
        }
    }
}

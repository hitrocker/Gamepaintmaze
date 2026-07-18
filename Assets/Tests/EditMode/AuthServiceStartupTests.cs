using NUnit.Framework;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class AuthServiceStartupTests
    {
        [Test]
        public void EnsureSessionSilently_BeforeFirebaseReady_QueuesWithoutBusyUi()
        {
            var gameObject = new GameObject("AuthServiceTest");
            var service = gameObject.AddComponent<AuthService>();
            bool busyEventRaised = false;
            bool failureRaised = false;
            service.BusyChanged += _ => busyEventRaised = true;
            service.Failed += _ => failureRaised = true;

            service.EnsureSessionSilently();

            Assert.IsTrue(service.IsSilentSessionPending);
            Assert.IsFalse(service.IsBusy);
            Assert.IsFalse(busyEventRaised);
            Assert.IsFalse(failureRaised);
            Object.DestroyImmediate(gameObject);
        }
    }
}

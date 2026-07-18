using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Game;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PaintMaze.Tests
{
    public class SwipeInputTests
    {
        [Test]
        public void DisabledInput_ReportsSwipeForBufferingWithoutExecutingIt()
        {
            var eventSystemObject = new GameObject("EventSystem");
            var inputObject = new GameObject("SwipeInput");
            try
            {
                var eventSystem = eventSystemObject.AddComponent<EventSystem>();
                var input = CreateInput(inputObject);
                input.Enabled = false;

                bool executed = false;
                bool buffered = false;
                SwipeDirection bufferedDirection = SwipeDirection.Left;
                input.Swiped += _ => executed = true;
                input.SwipedWhileDisabled += direction =>
                {
                    buffered = true;
                    bufferedDirection = direction;
                };

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                input.OnDrag(new PointerEventData(eventSystem) { position = Vector2.right * 100f });

                Assert.IsFalse(executed);
                Assert.IsTrue(buffered);
                Assert.That(bufferedDirection, Is.EqualTo(SwipeDirection.Right));
            }
            finally
            {
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void DragCrossingThreshold_EmitsBeforeReleaseWithoutDuplicatingOnRelease()
        {
            var eventSystemObject = new GameObject("EventSystem");
            var inputObject = new GameObject("SwipeInput");
            try
            {
                var eventSystem = eventSystemObject.AddComponent<EventSystem>();
                var input = CreateInput(inputObject);
                var swipes = new List<SwipeDirection>();
                input.Swiped += swipes.Add;

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                input.OnDrag(new PointerEventData(eventSystem) { position = Vector2.right * 100f });

                Assert.That(swipes, Is.EqualTo(new[] { SwipeDirection.Right }));

                input.OnPointerUp(new PointerEventData(eventSystem) { position = Vector2.right * 100f });
                Assert.That(swipes, Is.EqualTo(new[] { SwipeDirection.Right }));
            }
            finally
            {
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void HeldPointer_CanEmitMultipleDirectionsWithoutRelease()
        {
            var eventSystemObject = new GameObject("EventSystem");
            var inputObject = new GameObject("SwipeInput");
            try
            {
                var eventSystem = eventSystemObject.AddComponent<EventSystem>();
                var input = CreateInput(inputObject);
                var swipes = new List<SwipeDirection>();
                input.Swiped += swipes.Add;

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                input.OnDrag(new PointerEventData(eventSystem) { position = Vector2.right * 100f });
                input.OnDrag(new PointerEventData(eventSystem) {
                    position = Vector2.right * 100f + Vector2.up * 100f
                });

                Assert.That(swipes, Is.EqualTo(new[] {
                    SwipeDirection.Right, SwipeDirection.Up
                }));
            }
            finally
            {
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void MovementBelowThreshold_DoesNotEmit()
        {
            var eventSystemObject = new GameObject("EventSystem");
            var inputObject = new GameObject("SwipeInput");
            try
            {
                var eventSystem = eventSystemObject.AddComponent<EventSystem>();
                var input = CreateInput(inputObject);
                int swipes = 0;
                input.Swiped += _ => swipes++;

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                input.OnDrag(new PointerEventData(eventSystem) { position = Vector2.right * 10f });
                input.OnPointerUp(new PointerEventData(eventSystem) { position = Vector2.right * 20f });

                Assert.AreEqual(0, swipes);
            }
            finally
            {
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void PointerUp_RecognizesFastSwipeWithoutDragCallback()
        {
            var eventSystemObject = new GameObject("EventSystem");
            var inputObject = new GameObject("SwipeInput");
            try
            {
                var eventSystem = eventSystemObject.AddComponent<EventSystem>();
                var input = CreateInput(inputObject);
                SwipeDirection? swipe = null;
                input.Swiped += direction => swipe = direction;

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                input.OnPointerUp(new PointerEventData(eventSystem) { position = Vector2.up * 100f });

                Assert.That(swipe, Is.EqualTo(SwipeDirection.Up));
            }
            finally
            {
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void ScreenDeactivation_ClearsInterruptedPointerState()
        {
            var eventSystemObject = new GameObject("EventSystem");
            var inputObject = new GameObject("SwipeInput");
            try
            {
                var eventSystem = eventSystemObject.AddComponent<EventSystem>();
                var input = CreateInput(inputObject);
                int swipes = 0;
                input.Swiped += _ => swipes++;

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                // EditMode does not dispatch MonoBehaviour disable callbacks when a
                // test GameObject is toggled, so invoke the lifecycle method directly.
                typeof(SwipeInput).GetMethod(
                    "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(input, null);
                input.OnPointerUp(new PointerEventData(eventSystem) { position = Vector2.right * 100f });
                Assert.AreEqual(0, swipes, "stale pointer-up crossed the screen transition");

                input.OnPointerDown(new PointerEventData(eventSystem) { position = Vector2.zero });
                input.OnPointerUp(new PointerEventData(eventSystem) { position = Vector2.right * 100f });
                Assert.AreEqual(1, swipes, "fresh swipe was not accepted after reactivation");
            }
            finally
            {
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        private static SwipeInput CreateInput(GameObject inputObject)
        {
            var input = inputObject.AddComponent<SwipeInput>();
            typeof(SwipeInput).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(input, null);
            return input;
        }
    }
}

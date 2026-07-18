using NUnit.Framework;
using PaintMaze.Menu;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class CompletionFeedbackTests
    {
        [Test]
        public void CompleteController_PrewarmsAndStartsWithReusableSparkPool()
        {
            var root = new GameObject("CompletionTest", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<CompleteController>();
                controller.Build((RectTransform)root.transform);

                Assert.AreEqual(36, controller.SparkPoolCount);
                Assert.AreEqual(0, controller.ActiveSparkCount);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

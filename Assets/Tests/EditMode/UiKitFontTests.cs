using NUnit.Framework;
using PaintMaze.Services;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Tests
{
    public class UiKitFontTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("UiKitFontTest", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void Text_UsesBundledLexend()
        {
            Text text = UiKit.Text(
                _root.transform, "Heading", "PAINT MAZE", 48, Color.white);

            Assert.That(text.font.name, Does.Contain("Lexend"));
        }

        [Test]
        public void ItalicText_UsesLexendWithRequestedStyle()
        {
            Text text = UiKit.Text(
                _root.transform,
                "Italic",
                "Elegant",
                32,
                Color.white,
                TextAnchor.MiddleCenter,
                FontStyle.Italic);

            Assert.That(text.font.name, Does.Contain("Lexend"));
            Assert.AreEqual(FontStyle.Italic, text.fontStyle);
        }
    }
}

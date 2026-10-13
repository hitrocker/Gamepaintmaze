using NUnit.Framework;
using PaintMaze.Services;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Tests
{
    public class UiKitInputTests
    {
        private GameObject _root;
        private ThemeMode _originalTheme;

        [SetUp]
        public void SetUp()
        {
            _originalTheme = Theme.Mode;
            _root = new GameObject("UiKitInputTest", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            Theme.Mode = _originalTheme;
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [TestCase(ThemeMode.Dark)]
        [TestCase(ThemeMode.Light)]
        public void Input_UsesReadableTextAndCaretColors(ThemeMode mode)
        {
            Theme.Mode = mode;

            InputField field = UiKit.Input(
                _root.transform,
                "Email",
                "Email address",
                InputField.ContentType.EmailAddress,
                out Image background);

            Assert.AreEqual(Theme.CircleBg, background.color);
            Assert.AreEqual(Theme.OnCircle, field.textComponent.color);
            Assert.AreEqual(Theme.OnCircle, field.caretColor);
            Assert.AreNotEqual(background.color, field.textComponent.color);
        }

        [Test]
        public void PasswordInput_TogglesVisibilityWithoutClearingText()
        {
            InputField field = UiKit.Input(
                _root.transform,
                "Password",
                "Password",
                InputField.ContentType.Password,
                out Image background);
            field.text = "paint-maze-secret";

            Transform toggleTransform = background.transform.Find("PasswordVisibility");
            Assert.IsNotNull(toggleTransform);
            Button toggle = toggleTransform.GetComponent<Button>();
            Image icon = toggleTransform.Find("VisibilityIcon").GetComponent<Image>();

            Assert.AreEqual(InputField.ContentType.Password, field.contentType);
            Assert.AreEqual("EyeOpen", icon.sprite.name);

            toggle.onClick.Invoke();

            Assert.AreEqual(InputField.ContentType.Standard, field.contentType);
            Assert.AreEqual("paint-maze-secret", field.text);
            Assert.AreEqual("EyeCrossed", icon.sprite.name);

            toggle.onClick.Invoke();

            Assert.AreEqual(InputField.ContentType.Password, field.contentType);
            Assert.AreEqual("paint-maze-secret", field.text);
            Assert.AreEqual("EyeOpen", icon.sprite.name);
        }

        [Test]
        public void NonPasswordInput_DoesNotAddVisibilityButton()
        {
            UiKit.Input(
                _root.transform,
                "Email",
                "Email address",
                InputField.ContentType.EmailAddress,
                out Image background);

            Assert.IsNull(background.transform.Find("PasswordVisibility"));
        }
    }
}

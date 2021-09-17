using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor.Tests
{
    class AutoCompleteTests
    {
        EditorWindow m_Window;
        TextField m_TextField;
        AutoComplete m_AutoComplete;
        VisualElement m_CompletionBox;

        [OneTimeSetUp]
        public void GlobalSetUp()
        {
            m_Window = EditorWindow.CreateInstance<EditorWindow>();
            m_Window.Show();
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            m_Window.Close();
        }

        [SetUp]
        public void SetUp()
        {
            m_TextField = new TextField();
            m_Window.rootVisualElement.Add(m_TextField);
            m_AutoComplete = m_TextField.EnableAutoComplete(TestCompletionBehavior.Instance);
            m_CompletionBox = m_Window.rootVisualElement.Q(className: AutoComplete.AutoCompleteContainerUssClass);
            TestCompletionBehavior.Instance.CompletionItems = new List<string>
            {
                "Hello",
                "World",
                "Bonjour",
                "Bonsoir"
            };
        }

        [TearDown]
        public void Teardown()
        {
            m_Window.rootVisualElement.Clear();
        }

        [Test]
        public void AutoComplete_CompletionBoxCreated()
        {
            Assert.That(m_CompletionBox, Is.Not.Null);
        }

        [Test]
        public void AutoComplete_ShowCompletionBox()
        {
            m_TextField.Focus();
            m_TextField.Q(TextField.textInputUssName).Focus();

            m_TextField.SetValueWithoutNotify("Bon Hello");
            m_TextField.SelectRange(3, 3);
            m_Window.SendEvent(new Event
            {
                keyCode = KeyCode.Space,
                modifiers = EventModifiers.Control,
                type = EventType.KeyDown
            });

            Assert.That(m_CompletionBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(m_CompletionBox.Q<ListView>().itemsSource, Is.EquivalentTo(new[] { "Bonjour", "Bonsoir" }));
        }

        [Test]
        public void AutoComplete_HideCompletionBox()
        {
            AutoComplete_ShowCompletionBox();

            m_AutoComplete.Clear();
            Assert.That(m_CompletionBox.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void AutoComplete_CompletesToken()
        {
            m_TextField.Focus();
            m_TextField.Q(TextField.textInputUssName).Focus();

            m_TextField.SetValueWithoutNotify("Hello Wor 42");
            m_TextField.SelectRange(9, 9);
            m_Window.SendEvent(new Event
            {
                type = EventType.KeyDown,
                keyCode = KeyCode.Space,
                modifiers = EventModifiers.Control,
            });
            m_Window.SendEvent(new Event
            {
                type = EventType.KeyDown,
                keyCode = KeyCode.DownArrow
            });
            m_Window.SendEvent(new Event
            {
                type = EventType.KeyDown,
                keyCode = KeyCode.Return
            });

            Assert.That(m_TextField.text, Is.EqualTo("Hello World 42"));
        }

        class TestCompletionBehavior : AutoComplete.IAutoCompleteBehavior
        {
            static readonly Regex k_Regex = new Regex(@"\b(?<word>(\S)*)$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);
            static TestCompletionBehavior s_Instance;

            TestCompletionBehavior() { }

            public static TestCompletionBehavior Instance => s_Instance ?? (s_Instance = new TestCompletionBehavior());

            public List<string> CompletionItems = new List<string>();

            public bool ShouldStartAutoCompletion(string input, int caretPosition)
            {
                return GetToken(input, caretPosition).Length >= 1;
            }

            public string GetToken(string input, int caretPosition)
            {
                var match = k_Regex.Match(input, 0, caretPosition);
                if (!match.Success)
                    return string.Empty;

                var type = match.Groups["word"];
                return type.Value;
            }

            public IEnumerable<string> GetCompletionItems(string input, int caretPosition)
            {
                var token = GetToken(input, caretPosition);
                return CompletionItems.Where(s => s.StartsWith(token, StringComparison.OrdinalIgnoreCase));
            }

            public (string newInput, int caretPosition) OnCompletion(string completedToken, string input, int caretPosition)
            {
                var match = k_Regex.Match(input, 0, caretPosition);
                var componentType = match.Groups["word"];

                var final = input.Substring(0, componentType.Index) + completedToken + input.Substring(componentType.Index + componentType.Length);
                return (final, componentType.Index + completedToken.Length);
            }
        }
    }
}

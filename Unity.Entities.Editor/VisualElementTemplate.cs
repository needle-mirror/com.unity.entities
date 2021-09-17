using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Utility class to handle loading and cloning a <see cref="VisualTreeAsset"/>, along with its <see cref="StyleSheet"/>.
    /// </summary>
    class VisualElementTemplate
    {
        readonly string m_Name;
        readonly Lazy<VisualTreeAsset> m_Template;
        readonly Lazy<StyleSheet> m_StyleSheet;
        readonly Lazy<StyleSheet> m_StyleSheetVariant;

        /// <summary>
        /// Construct a new VisualElementTemplate.
        /// </summary>
        /// <param name="name">The name of the uxml asset, without extension.</param>
        public VisualElementTemplate(string name)
        {
            m_Name = name;
            m_Template = new Lazy<VisualTreeAsset>(() => EditorResources.Load<VisualTreeAsset>($"uxml/{name}.uxml", true));
            m_StyleSheet = new Lazy<StyleSheet>(() => EditorResources.Load<StyleSheet>($"uss/{name}.uss", false));
            m_StyleSheetVariant = new Lazy<StyleSheet>(() => EditorResources.Load<StyleSheet>($"uss/{name}_{(EditorGUIUtility.isProSkin ? "dark" : "light")}.uss", false));
        }

        /// <summary>
        /// Construct a new VisualElementTemplate, and load assets from a package.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="name">The name of the uxml asset, without extension.</param>
        public VisualElementTemplate(string packageId, string name)
        {
            m_Name = name;
            m_Template = new Lazy<VisualTreeAsset>(() => EditorResources.Load<VisualTreeAsset>(packageId, $"uxml/{name}.uxml", true));
            m_StyleSheet = new Lazy<StyleSheet>(() => EditorResources.Load<StyleSheet>(packageId, $"uss/{name}.uss", false));
            m_StyleSheetVariant = new Lazy<StyleSheet>(() => EditorResources.Load<StyleSheet>(packageId, $"uss/{name}_{(EditorGUIUtility.isProSkin ? "dark" : "light")}.uss", false));
        }

        /// <summary>
        /// Instantiate the visual element template.
        /// </summary>
        /// <param name="root">Optional parent of the visual element template.</param>
        /// <returns>An instance of the visual element template.</returns>
        public VisualElement Clone(VisualElement root = null)
        {
            root = CloneTemplate(root);
            AddStyles(root);
            return root;
        }

        /// <summary>
        /// Add this visual element template styles to the specified visual element.
        /// </summary>
        /// <param name="element">The visual element to add styles to.</param>
        public void AddStyles(VisualElement element)
        {
            if (m_StyleSheet.Value != null)
            {
                element.styleSheets.Add(m_StyleSheet.Value);
                element.AddToClassList(m_Name.Substring(m_Name.LastIndexOf('/') + 1));
            }

            if (m_StyleSheetVariant.Value != null)
                element.styleSheets.Add(m_StyleSheetVariant.Value);
        }

        /// <summary>
        /// Remove this visual element template styles from the specified visual element.
        /// </summary>
        /// <param name="element">The visual element to remove styles from.</param>
        public void RemoveStyles(VisualElement element)
        {
            if (m_StyleSheet.Value != null)
            {
                element.styleSheets.Remove(m_StyleSheet.Value);
                element.RemoveFromClassList(m_Name);
            }

            if (m_StyleSheetVariant.Value != null)
                element.styleSheets.Remove(m_StyleSheetVariant.Value);
        }

        VisualElement CloneTemplate(VisualElement element)
        {
            if (element == null)
                return m_Template.Value.CloneTree();

            m_Template.Value.CloneTree(element);
            return element;
        }
    }
}

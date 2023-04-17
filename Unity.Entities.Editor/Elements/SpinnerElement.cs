using System;
using JetBrains.Annotations;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class SpinnerElement : VisualElement
    {
        /// <summary>
        /// Instantiates a <see cref="SpinnerElement"/> using the data read from a UXML file.
        /// </summary>
        [UsedImplicitly]
        class SearchElementFactory : UxmlFactory<SpinnerElement, SpinnerElementTraits>
        {
            
        }

        /// <summary>
        /// Defines UxmlTraits for the SpinnerElement.
        /// </summary>
        [UsedImplicitly]
        class SpinnerElementTraits : UxmlTraits
        {
        }

        static readonly VisualElementTemplate k_Template = new(Resources.PackageId, "Spinner/spinner");

        readonly IVisualElementScheduledItem m_ScheduledItem;
        
        int m_Index;

        public SpinnerElement()
        {
            k_Template.AddStyles(this);
            
            AddToClassList("spinner");
            AddToClassList("spinner-icons");
            AddToClassList(GetSpinnerClass(m_Index));
            m_ScheduledItem = schedule.Execute(UpdateSpinner).Every(Convert.ToInt64(1000.0f / 12.0f));
        }

        static string GetSpinnerClass(int index)
            => $"spinner-background-image-{index}";
            
        void UpdateSpinner(TimerState obj)
        {
            RemoveFromClassList(GetSpinnerClass(m_Index));
            m_Index = (m_Index + 1) % 12;
            AddToClassList(GetSpinnerClass(m_Index));
        }

        public void Resume()
        {
            m_ScheduledItem.Resume();
        }

        public void Pause()
        {
            m_ScheduledItem.Pause();
        }
    }
}
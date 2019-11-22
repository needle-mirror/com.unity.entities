using UnityEngine;
using System.Linq;


namespace Unity.Build.Common.Tests
{
    public class ResultContainer : ScriptableObject
    {
        [SerializeField]
        private string[] m_Results;

        [SerializeField]
        private bool m_Completed;

        public string[] Results
        {
            set
            {
                m_Results = value;
            }
            get
            {
                return m_Results;
            }
        }

        public bool Completed
        {
            set
            {
                m_Completed = value;
            }
            get
            {
                return m_Completed;
            }
        }

        private string GetMessage(BuildPipelineResult result)
        {
            var msg = result.Succeeded ? "Success" : "Fail";
            return $"{result.BuildSettings.name}, {msg}";
        }
        public void SetCompleted(BuildPipelineResult[] results)
        {
            m_Results = results.Select(r => GetMessage(r)).ToArray();
            m_Completed = true;
        }
    }
}

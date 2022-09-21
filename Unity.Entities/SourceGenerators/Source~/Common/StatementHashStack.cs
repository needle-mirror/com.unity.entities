using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.Common
{
    /// <summary>
    /// Stack to store statements hashed, without duplicates belonging to a statement.
    /// When you pop you get back a list of statements, that all correspond with the same statement.
    /// </summary>
    public struct StatementHashStack
    {
        public static StatementHashStack CreateInstance() =>
            new StatementHashStack
            {
                m_GiveBack = new List<string>(10),
                m_LocationInfo = new List<(StatementSyntax, int)>(10),
            };

        List<string> m_GiveBack;
        List<(StatementSyntax statement, int endingIndex)> m_LocationInfo;
        public StatementSyntax ActiveStatement;
        int m_CurrentIndex;


        /// <summary>
        /// Pushes a new statement onto the stack.
        /// If the last statement matches the current, it'll be part of the same blob.
        /// </summary>
        /// <param name="statement">Statement</param>
        /// <param name="statementString">Statement parsable snippet</param>
        public void PushStatement(StatementSyntax statement, string statementString)
        {
            if (ActiveStatement != statement)
            {
                m_CurrentIndex = m_LocationInfo.Count > 0 ? m_LocationInfo[m_LocationInfo.Count - 1].endingIndex : 0;
                m_LocationInfo.Add((statement, m_CurrentIndex));
                ActiveStatement = statement;
            }

            var containedInGiveBack = false;
            var statementLocationInfo = m_LocationInfo[m_LocationInfo.Count - 1];
            for (var i = m_CurrentIndex; i < statementLocationInfo.endingIndex; i++)
            {
                containedInGiveBack |= m_GiveBack[i] == statementString;
            }

            if (!containedInGiveBack)
            {
                m_GiveBack.Add(statementString);
                statementLocationInfo.endingIndex++;
                m_LocationInfo[m_LocationInfo.Count - 1] = statementLocationInfo;
            }
        }

        /// <summary>
        /// Returns all statements belonging to the same hash used for the current push
        /// ie (a,6), (b,5), (c,5), would return b,c and the stack would now just be (a,6)
        /// </summary>
        /// <returns>Statements with equal statement hash</returns>
        public List<StatementSyntax> PopSyntax()
        {
            var latestLocation = m_LocationInfo[m_LocationInfo.Count - 1];
            m_LocationInfo.RemoveAt(m_LocationInfo.Count - 1);

            if (m_LocationInfo.Count > 0)
            {
                var previousLocation = m_LocationInfo[m_LocationInfo.Count - 1];
                ActiveStatement = previousLocation.statement;
                m_CurrentIndex = previousLocation.endingIndex;
            }
            else
            {
                ActiveStatement = null;
                m_CurrentIndex = 0;
            }

            var returnList = new List<StatementSyntax>(4);
            for (var index = latestLocation.endingIndex - 1; index >= m_CurrentIndex; index--)
            {
                returnList.Add(SyntaxFactory.ParseStatement(m_GiveBack[index]).WithHiddenLineTrivia() as StatementSyntax);
                m_GiveBack.RemoveAt(index);
            }

            return returnList;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor.Tests.LiveLink
{
    [TestFixture]
    class LiveLinkDropdownTests
    {
        [Test]
        public void LiveLinkConnection_PublishEvents()
        {
            var nameChanges = new List<LiveLinkConnectionsDropdown.LiveLinkConnection>();
            var statusChanges = new List<(LiveLinkConnectionsDropdown.LiveLinkConnection, LiveLinkConnectionsDropdown.LiveLinkConnectionStatus)>();
            var buildSettingsChanges = new List<LiveLinkConnectionsDropdown.LiveLinkConnection>();
            var c = new LiveLinkConnectionsDropdown.LiveLinkConnection(1, "name", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, new Hash128(Guid.NewGuid().ToString("N")));
            c.NameChanged += sender => nameChanges.Add(sender);
            c.StatusChanged += (sender, previousStatus) => statusChanges.Add((sender, previousStatus));
            c.BuildSettingsGuidChanged += sender => buildSettingsChanges.Add(sender);

            c.Name = "another name";
            Assert.That(nameChanges, Is.EquivalentTo(new[] { c }));
            Assert.That(c.Name, Is.EqualTo("another name"));
            Assert.That(statusChanges, Is.Empty);
            Assert.That(buildSettingsChanges, Is.Empty);
            nameChanges.Clear();

            c.Status = LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Disabled;
            Assert.That(statusChanges, Is.EquivalentTo(new[] { (c, LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected) }));
            Assert.That(c.Status, Is.EqualTo(LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Disabled));
            Assert.That(nameChanges, Is.Empty);
            Assert.That(buildSettingsChanges, Is.Empty);
            statusChanges.Clear();

            var otherBuildSettingsGuid = new Hash128(Guid.NewGuid().ToString("N"));
            c.BuildSettingsGuid = otherBuildSettingsGuid;
            Assert.That(buildSettingsChanges, Is.EquivalentTo(new[] { c }));
            Assert.That(c.BuildSettingsGuid, Is.EqualTo(otherBuildSettingsGuid));
            Assert.That(statusChanges, Is.Empty);
            Assert.That(nameChanges, Is.Empty);
            buildSettingsChanges.Clear();
        }

        [Test]
        public void LiveLinkConnection_DontPublishEventsWhenNothingChanges()
        {
            var c = new LiveLinkConnectionsDropdown.LiveLinkConnection(1, "name", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, new Hash128(Guid.NewGuid().ToString("N")));
            c.NameChanged += delegate { Assert.Fail($"{nameof(c.NameChanged)} event shouldn't be fired"); };
            c.StatusChanged += delegate { Assert.Fail($"{nameof(c.StatusChanged)} event shouldn't be fired"); };
            c.BuildSettingsGuidChanged += delegate { Assert.Fail($"{nameof(c.BuildSettingsGuidChanged)} event shouldn't be fired"); };

            c.Name = c.Name;
            c.Status = c.Status;
            c.BuildSettingsGuid = c.BuildSettingsGuid;
        }

        [Test]
        public void LiveLinkConnectionsView_GenerateValidUI()
        {
            var settingGuid1 = new Hash128(Guid.NewGuid().ToString("N"));
            var settingGuid2 = new Hash128(Guid.NewGuid().ToString("N"));
            var connections = new ObservableCollection<LiveLinkConnectionsDropdown.LiveLinkConnection>
            {
                new LiveLinkConnectionsDropdown.LiveLinkConnection(1, "player 1", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, settingGuid1),
                new LiveLinkConnectionsDropdown.LiveLinkConnection(2, "player 2", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, settingGuid1),
                new LiveLinkConnectionsDropdown.LiveLinkConnection(3, "player 3", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, settingGuid2),
            };

            var view = new LiveLinkConnectionsDropdown.LiveLinkConnectionsView(connections, hash128 => hash128.ToString());
            var root = new VisualElement();
            view.BuildFullUI(root);

            // Ensure groups are visible and empty message hidden
            var groupsContainer = GetItem<VisualElement>(root, "live-link-connections-dropdown__Groups");
            Assert.That(groupsContainer.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(GetItem<VisualElement>(root, "live-link-connections-dropdown__EmptyMessage").style.display.value, Is.EqualTo(DisplayStyle.None));

            // Ensure there are 2 groups and they have the correct names
            var groups = GetItems<VisualElement>(groupsContainer, className: "live-link-connections-dropdown__groups__item");
            Assert.That(groups.Count, Is.EqualTo(2));
            var groupsNames = groupsContainer.Query<VisualElement>(className: "live-link-connections-dropdown__groups__item__title").Children<Label>().ToList().Select(l => l.text).ToArray();
            Assert.That(groupsNames, Is.EquivalentTo(new[] { settingGuid1.ToString(), settingGuid2.ToString() }));

            // Ensure each group contains the correct player connections
            foreach (var g in groups)
            {
                var names = g.Query<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Children<Label>().ToList().Select(l => l.text).ToArray();
                var groupName = g.Q<Label>().text;
                if (groupName == settingGuid1.ToString())
                    Assert.That(names, Is.EquivalentTo(new[] { connections[0].Name, connections[1].Name }));
                else if (groupName == settingGuid2.ToString())
                    Assert.That(names, Is.EquivalentTo(new[] { connections[2].Name }));
                else
                    Assert.Fail("Unexpected group name");
            }
        }

        [Test]
        public void LiveLinkConnectionsView_UpdateConnectionName()
        {
            var connections = new ObservableCollection<LiveLinkConnectionsDropdown.LiveLinkConnection>
            {
                new LiveLinkConnectionsDropdown.LiveLinkConnection(1, "player 1", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, new Hash128(Guid.NewGuid().ToString("N"))),
            };

            var view = new LiveLinkConnectionsDropdown.LiveLinkConnectionsView(connections, hash128 => hash128.ToString());
            var root = new VisualElement();
            view.BuildFullUI(root);

            var name = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Label>().text;
            Assert.That(name, Is.EqualTo("player 1"));

            connections[0].Name = "player 1 changed";
            name = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Label>().text;
            Assert.That(name, Is.EqualTo("player 1 changed"));
        }

        [Test]
        public void LiveLinkConnectionsView_UpdateConnectionStatus()
        {
            var connections = new ObservableCollection<LiveLinkConnectionsDropdown.LiveLinkConnection>
            {
                new LiveLinkConnectionsDropdown.LiveLinkConnection(1, "player 1", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, new Hash128(Guid.NewGuid().ToString("N"))),
            };

            var view = new LiveLinkConnectionsDropdown.LiveLinkConnectionsView(connections, hash128 => hash128.ToString());
            var root = new VisualElement();
            view.BuildFullUI(root);

            var iconCls = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Image>().GetClasses().Single();
            Assert.That(iconCls, Is.EqualTo("live-link-connections-dropdown__status--connected"));

            connections[0].Status = LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Disabled;
            iconCls = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Image>().GetClasses().Single();
            Assert.That(iconCls, Is.EqualTo("live-link-connections-dropdown__status--disabled"));

            connections[0].Status = LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Reseting;
            iconCls = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Image>().GetClasses().Single();
            Assert.That(iconCls, Is.EqualTo("live-link-connections-dropdown__status--reseting"));

            connections[0].Status = LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Error;
            iconCls = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Image>().GetClasses().Single();
            Assert.That(iconCls, Is.EqualTo("live-link-connections-dropdown__status--error"));

            connections[0].Status = LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected;
            iconCls = root.Q<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Q<Image>().GetClasses().Single();
            Assert.That(iconCls, Is.EqualTo("live-link-connections-dropdown__status--connected"));
        }

        [Test]
        public void LiveLinkConnectionsView_UpdateGroupingWhenBuildSettingsGuidChange()
        {
            var settingGuid1 = new Hash128(Guid.NewGuid().ToString("N"));
            var settingGuid2 = new Hash128(Guid.NewGuid().ToString("N"));
            var settingGuid3 = new Hash128(Guid.NewGuid().ToString("N"));
            var connections = new ObservableCollection<LiveLinkConnectionsDropdown.LiveLinkConnection>
            {
                new LiveLinkConnectionsDropdown.LiveLinkConnection(1, "player 1", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, settingGuid1),
                new LiveLinkConnectionsDropdown.LiveLinkConnection(2, "player 2", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, settingGuid1),
                new LiveLinkConnectionsDropdown.LiveLinkConnection(3, "player 3", LiveLinkConnectionsDropdown.LiveLinkConnectionStatus.Connected, settingGuid2),
            };

            var view = new LiveLinkConnectionsDropdown.LiveLinkConnectionsView(connections, hash128 => hash128.ToString());
            var root = new VisualElement();
            view.BuildFullUI(root);

            var groups = root.Query<VisualElement>(className: "live-link-connections-dropdown__groups__item").ToList();
            foreach (var g in groups)
            {
                var playerNames = g.Query<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Children<Label>().ToList().Select(l => l.text).ToArray();
                var name = g.Q<Label>().text;
                if (name == settingGuid1.ToString())
                    Assert.That(playerNames, Is.EqualTo(new[] { connections[0].Name, connections[1].Name }));
                else if(name == settingGuid2.ToString())
                    Assert.That(playerNames, Is.EqualTo(new[] { connections[2].Name }));
                else
                    Assert.Fail("Unexpected group name");
            }

            connections[0].BuildSettingsGuid = settingGuid3;
            connections[1].BuildSettingsGuid = settingGuid2;

            groups = root.Query<VisualElement>(className: "live-link-connections-dropdown__groups__item").ToList();
            foreach (var g in groups)
            {
                var playerNames = g.Query<VisualElement>(className: "live-link-connections-dropdown__groups__item__device").Children<Label>().ToList().Select(l => l.text).ToArray();
                var name = g.Q<Label>().text;
                if (name == settingGuid3.ToString())
                    Assert.That(playerNames, Is.EqualTo(new[] { connections[0].Name }));
                else if(name == settingGuid2.ToString())
                    Assert.That(playerNames, Is.EqualTo(new[] { connections[1].Name, connections[2].Name }));
                else
                    Assert.Fail("Unexpected group name");
            }
        }

        [Test]
        public void LiveLinkConnectionsView_GenerateValidEmptyUI()
        {
            var connections = new ObservableCollection<LiveLinkConnectionsDropdown.LiveLinkConnection>();
            var view = new LiveLinkConnectionsDropdown.LiveLinkConnectionsView(connections, hash128 => hash128.ToString());
            var root = new VisualElement();
            view.BuildFullUI(root);

            Assert.That(GetItem<VisualElement>(root, "live-link-connections-dropdown__Groups").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(GetItem<VisualElement>(root, "live-link-connections-dropdown__EmptyMessage").style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        static T GetItem<T>(VisualElement v, string name = null, string className = null) where T : VisualElement
        {
            var el = v.Q<T>(name: name, className: className);
            Assert.That(el, Is.Not.Null);
            return el;
        }

        static List<T> GetItems<T>(VisualElement v, string name = null, string className = null) where T : VisualElement
        {
            var el = v.Query<T>(name: name, className: className).ToList();
            Assert.That(el, Is.Not.Empty);
            return el;
        }
    }
}
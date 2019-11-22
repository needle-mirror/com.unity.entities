using NUnit.Framework;

namespace Unity.Build.Tests
{
    class BuildContextTests
    {
        class TestA { }
        class TestB { }

        [Test]
        public void HasValue()
        {
            var context = new BuildContext();
            context.SetValue(new TestA());
            Assert.That(context.HasValue<TestA>(), Is.True);
            Assert.That(context.HasValue<TestB>(), Is.False);
        }

        [Test]
        public void GetValue_ReturnNull_WhenValueDoesNotExist()
        {
            var context = new BuildContext();
            Assert.That(context.GetValue<TestA>(), Is.Null);
        }

        [Test]
        public void GetValue_DoesNotThrow_WhenValueDoesNotExist()
        {
            var context = new BuildContext();
            Assert.DoesNotThrow(() => context.GetValue<TestA>());
        }

        [Test]
        public void GetOrCreateValue()
        {
            var context = new BuildContext();
            Assert.That(context.GetOrCreateValue<TestA>(), Is.Not.Null);
            Assert.That(context.HasValue<TestA>(), Is.True);
            Assert.That(context.GetValue<TestA>(), Is.Not.Null);
            Assert.That(context.Values.Length, Is.EqualTo(1));
        }

        [Test]
        public void GetOrCreateValue_DoesNotThrow_WhenValueExist()
        {
            var context = new BuildContext();
            context.SetValue(new TestA());
            Assert.DoesNotThrow(() => context.GetOrCreateValue<TestA>());
        }

        [Test]
        public void SetValue()
        {
            var context = new BuildContext();
            context.SetValue(new TestA());
            Assert.That(context.HasValue<TestA>(), Is.True);
            Assert.That(context.GetValue<TestA>(), Is.Not.Null);
            Assert.That(context.Values.Length, Is.EqualTo(1));
        }

        [Test]
        public void SetValue_SkipNullValues()
        {
            var context = new BuildContext();
            object value = null;
            context.SetValue(value);
            Assert.That(context.Values.Length, Is.Zero);
        }

        [Test]
        public void SetValue_OverrideValue_WhenValueExist()
        {
            var context = new BuildContext();
            var instance1 = new TestA();
            var instance2 = new TestA();

            context.SetValue(instance1);
            Assert.That(context.Values, Is.EqualTo(new[] { instance1 }));

            context.SetValue(instance2);
            Assert.That(context.Values, Is.EqualTo(new[] { instance2 }));
        }

        [Test]
        public void RemoveValue()
        {
            var context = new BuildContext();
            context.SetValue(new TestA());
            Assert.That(context.Values.Length, Is.EqualTo(1));

            context.RemoveValue<TestA>();
            Assert.That(context.Values.Length, Is.Zero);
        }
    }
}

#if !UNITY_DOTSPLAYER
using System;
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
using Random = Unity.Mathematics.Random;

namespace Entities.Tests
{

    [TestFixture("en-US")]
    [TestFixture("da-DK")]
    internal class EntityNameStorageTests
    {
        CultureInfo testCulture;
        CultureInfo backupCulture;
        World world;
        EntityNameStorage.State backupEntityNameState;
        private NativeArray<FixedString64Bytes> backupNames;
        public EntityNameStorageTests(string culture)
        {
            testCulture = CultureInfo.CreateSpecificCulture(culture);
        }

        [SetUp]
        public virtual unsafe void Setup()
        {
            backupCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = testCulture;

            world = new World("Test World");
#if !DOTS_DISABLE_DEBUG_NAMES
            //backup name storage, for validation
            backupEntityNameState = EntityNameStorage.s_State.Data;
            //set to false so initialize fully clears data, as if it's a new playmode.
            EntityNameStorage.s_State.Data.initialized = 0;
            EntityNameStorage.Initialize();
            Assert.AreEqual(1,EntityNameStorage.s_State.Data.entries);
#endif //!DOTS_DISABLE_DEBUG_NAMES
        }

        [TearDown]
        public virtual unsafe void TearDown()
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            //restore name storage
            EntityNameStorage.Shutdown();
            EntityNameStorage.s_State.Data = backupEntityNameState;
#endif //DOTS_DISABLE_DEBUG_NAMES
            world.Dispose();
            Thread.CurrentThread.CurrentCulture = backupCulture;
        }

#if !DOTS_DISABLE_DEBUG_NAMES
        [Test]
        public void EntityName_TestCapacity()
        {
            Random random = new Random(0x1234);
            unsafe string RandomBigName()
            {
                var name = stackalloc char[256];
                for (int i = 0; i < 256; i++)
                {
                    name[i] = (char) random.NextInt('a', 'z');
                }

                return new string(name);
            }

            int capacity = EntityNameStorage.kMaxEntries;
            //because the first entry in the storage is 0 for "empty string"
            for (int i = 0; i < capacity - 1; i++)
            {
                Entity e = world.EntityManager.CreateEntity();
                world.EntityManager.SetName(e,RandomBigName());
            }
            Assert.AreEqual(capacity,EntityNameStorage.s_State.Data.entries);
            Assert.LessOrEqual(EntityNameStorage.s_State.Data.chars,EntityNameStorage.kMaxChars);

            Entity overE = world.EntityManager.CreateEntity();
            world.EntityManager.SetName(overE,"Overflow Entity");
            Assert.AreEqual(capacity,EntityNameStorage.s_State.Data.entries);

            LogAssert.Expect(LogType.Error,EntityNameStorage.s_State.Data.kMaxEntriesMsg.ToString());

        }

        [Test]
        public void EntityName_Truncates_Long_Strings()
        {
            //make a string 1 character longer than FixedString64Bytes.utf8MaxLengthInBytes - 1
            string longString = "This is a string that is exactly 62 characters, paddddddddingX";

            Entity overE = world.EntityManager.CreateEntity();
            world.EntityManager.SetName(overE,longString);
            string truncName = world.EntityManager.GetName(overE);
            Assert.AreEqual(FixedString64Bytes.utf8MaxLengthInBytes,truncName.Length);
            //note the X is truncated
            Assert.AreEqual("This is a string that is exactly 62 characters, padddddddding",truncName);
        }

#endif //!DOTS_DISABLE_DEBUG_NAMES
        [TestCase("red")]
        [TestCase("orange")]
        [TestCase("yellow")]
        [TestCase("green")]
        [TestCase("blue")]
        [TestCase("indigo")]
        [TestCase("violet")]
        [TestCase("紅色", TestName="{m}(Chinese-Red)")]
        [TestCase("橙色", TestName="{m}(Chinese-Orange)")]
        [TestCase("黄色", TestName="{m}(Chinese-Yellow)")]
        [TestCase("绿色", TestName="{m}(Chinese-Green)")]
        [TestCase("蓝色", TestName="{m}(Chinese-Blue")]
        [TestCase("靛蓝色", TestName="{m}(Chinese-Indigo")]
        [TestCase("紫罗兰色", TestName="{m}(Chinese-Violet")]
        [TestCase("црвена", TestName = "{m}(Serbian-Red)")]
        [TestCase("наранџаста", TestName = "{m}(Serbian-Orange)")]
        [TestCase("жута", TestName = "{m}(Serbian-Yellow)")]
        [TestCase("зелена", TestName = "{m}(Serbian-Green)")]
        [TestCase("плава", TestName = "{m}(Serbian-Blue")]
        [TestCase("индиго", TestName = "{m}(Serbian-Indigo")]
        [TestCase("љубичаста", TestName = "{m}(Serbian-Violet")]
        [TestCase("George Washington")]
        [TestCase("John Adams")]
        [TestCase("Thomas Jefferson")]
        [TestCase("James Madison")]
        [TestCase("James Monroe")]
        [TestCase("John Quincy Adams")]
        [TestCase("Andrew Jackson")]
        [TestCase("村上春樹", TestName="{m}(HarukiMurakami)")]
        [TestCase("三島 由紀夫", TestName="{m}(MishimaYukio)")]
        [TestCase("吉本ばなな", TestName="{m}(YoshimotoBanana)")]
        [TestCase("大江健三郎", TestName="{m}(OeKenzaburo)")]
        [TestCase("川端 康成", TestName="{m}(KawabataYasunari)")]
        [TestCase("桐野夏生", TestName="{m}(TongyeXiasheng)")]
        [TestCase("芥川龍之介", TestName="{m}(RyunosukeAkutagawa)")]
        [TestCase("로마는 하루아침에 이루어진 것이 아니다", TestName="{m}(Korean-Proverb1)")]
        [TestCase("낮말은 새가 듣고 밤말은 쥐가 듣는다", TestName="{m}(Korean-Proverb2)")]
        [TestCase("말을 냇가에 끌고 갈 수는 있어도 억지로 물을 먹일 수는 없다", TestName="{m}(Korean-Proverb3)")]
        [TestCase("호랑이에게 물려가도 정신만 차리면 산다", TestName="{m}(Korean-Proverb4)")]
        [TestCase("Љубазни фењерџија чађавог лица хоће да ми покаже штос.", TestName = "{m}(Serbian-Pangram)")]
        [TestCase("Лако ти је плитку воду замутити и будалу наљутити", TestName = "{m}(Serbian-Proverb)")]
        [TestCase("Үнэн үг хэлсэн хүнд ноёд өстэй, үхэр унасан хүнд ноход өстэй.", TestName="{m}(Mongolian-Proverb1)")]
        [TestCase("Өнгөрсөн борооны хойноос эсгий нөмрөх.", TestName="{m}(Mongolian-Proverb2)")]
        [TestCase("Барын сүүл байснаас батганы толгой байсан нь дээр.", TestName="{m}(Mongolian-Proverb3)")]
        [TestCase("Гараар ганц хүнийг дийлэх. Tолгойгоор мянган хүнийг дийлэх.", TestName="{m}(Mongolian-Proverb4)")]
        [TestCase("Աղւէսը բերանը խաղողին չի հասնում, ասում է՝ խակ է", TestName="{m}(Armenian-Proverb1)")]
        [TestCase("Ամեն փայտ շերեփ չի դառնա, ամեն սար՝ Մասիս", TestName="{m}(Armenian-Proverb2)")]
        [TestCase("Արևին ասում է դուրս մի արի՝ ես դուրս եմ եկել", TestName="{m}(Armenian-Proverb3)")]
        [TestCase("Գայլի գլխին Աւետարան են կարդում, ասում է՝ շուտ արէ՛ք, գալլէս գնաց", TestName="{m}(Armenian-Proverb4)")]
        [TestCase("पृथिव्यां त्रीणी रत्नानि जलमन्नं सुभाषितम्।", TestName="{m}(Hindi-Proverb1)")]
        [TestCase("जननी जन्मभुमिस्छ स्वर्गादपि गरीयसि", TestName="{m}(Hindi-Proverb2)")]
        [TestCase("न अभिशेको न संस्कारः सिम्हस्य कृयते वनेविक्रमार्जितसत्वस्य स्वयमेव मृगेन्द्रता", TestName="{m}(Hindi-Proverb3)")]
        public void EntityName_AddWorks(String value)
        {
            // TODO: this test should run on a sandboxed new EntityNameStorage instance, so it doesn't inherit/corrupt the existing global name storage
            EntityName w = new EntityName();
            //Assert.IsFalse(EntityNameStorage.Contains(value));
            //Assert.IsTrue(EntityNameStorage.Entries == 1);
            w.SetString(value);

            String finalValue = value;

            var utf8 = new UTF8Encoding();
            var encoder = utf8.GetEncoder();
            unsafe
            {
                fixed (char* chars = value)
                {
                    byte[] finalBytes = new byte[FixedString64Bytes.utf8MaxLengthInBytes];
                    int byteLen = -1;
                    int charsUsed = -1;
                    int bytesUsed = -1;
                    bool completed = false;
                    fixed (byte* bytes = finalBytes)
                    {
                        encoder.Convert(chars, value.Length, bytes, FixedString64Bytes.utf8MaxLengthInBytes,
                            true, out charsUsed, out bytesUsed, out completed);
                        byteLen = bytesUsed;
                    }

                    finalValue = Encoding.Default.GetString(finalBytes,0,bytesUsed);
                }
            }
            Debug.Log($"{finalValue} vs. {w.ToString()}");
            Debug.Log($"{finalValue.Length} vs. {w.ToString().Length}");

            Assert.IsTrue(EntityNameStorage.Contains(finalValue));
            //Assert.IsTrue(EntityNameStorage.Entries == 2);
        }


        [TestCase("red")]
    [TestCase("orange")]
    [TestCase("yellow")]
    [TestCase("green")]
    [TestCase("blue")]
    [TestCase("indigo")]
    [TestCase("violet")]
    [TestCase("紅色", TestName="{m}(Chinese-Red)")]
    [TestCase("橙色", TestName="{m}(Chinese-Orange)")]
    [TestCase("黄色", TestName="{m}(Chinese-Yellow)")]
    [TestCase("绿色", TestName="{m}(Chinese-Green)")]
    [TestCase("蓝色", TestName="{m}(Chinese-Blue")]
    [TestCase("靛蓝色", TestName="{m}(Chinese-Indigo")]
    [TestCase("紫罗兰色", TestName="{m}(Chinese-Violet")]
    [TestCase("црвена", TestName = "{m}(Serbian-Red)")]
    [TestCase("наранџаста", TestName = "{m}(Serbian-Orange)")]
    [TestCase("жута", TestName = "{m}(Serbian-Yellow)")]
    [TestCase("зелена", TestName = "{m}(Serbian-Green)")]
    [TestCase("плава", TestName = "{m}(Serbian-Blue")]
    [TestCase("индиго", TestName = "{m}(Serbian-Indigo")]
    [TestCase("љубичаста", TestName = "{m}(Serbian-Violet")]
    [TestCase("George Washington")]
    [TestCase("John Adams")]
    [TestCase("Thomas Jefferson")]
    [TestCase("James Madison")]
    [TestCase("James Monroe")]
    [TestCase("John Quincy Adams")]
    [TestCase("Andrew Jackson")]
    [TestCase("村上春樹", TestName="{m}(HarukiMurakami)")]
    [TestCase("三島 由紀夫", TestName="{m}(MishimaYukio)")]
    [TestCase("吉本ばなな", TestName="{m}(YoshimotoBanana)")]
    [TestCase("大江健三郎", TestName="{m}(OeKenzaburo)")]
    [TestCase("川端 康成", TestName="{m}(KawabataYasunari)")]
    [TestCase("桐野夏生", TestName="{m}(TongyeXiasheng)")]
    [TestCase("芥川龍之介", TestName="{m}(RyunosukeAkutagawa)")]
    [TestCase("로마는 하루아침에 이루어진 것이 아니다", TestName="{m}(Korean - Rome was not made overnight)")]
    [TestCase("낮말은 새가 듣고 밤말은 쥐가 듣는다", TestName="{m}(Korean-Proverb2)")]
    [TestCase("말을 냇가에 끌고 갈 수는 있어도 억지로 물을 먹일 수는 없다", TestName="{m}(Korean-Proverb3)")]
    [TestCase("호랑이에게 물려가도 정신만 차리면 산다", TestName="{m}(Korean-Proverb4)")]
    [TestCase("Љубазни фењерџија чађавог лица хоће да ми покаже штос.", TestName = "{m}(Serbian-Pangram)")]
    [TestCase("Лако ти је плитку воду замутити и будалу наљутити", TestName = "{m}(Serbian-Proverb)")]
    [TestCase("Үнэн үг хэлсэн хүнд ноёд өстэй, үхэр унасан хүнд ноход өстэй.", TestName="{m}(Mongolian-Proverb1)")]
    [TestCase("Өнгөрсөн борооны хойноос эсгий нөмрөх.", TestName="{m}(Mongolian-Proverb2)")]
    [TestCase("Барын сүүл байснаас батганы толгой байсан нь дээр.", TestName="{m}(Mongolian-Proverb3)")]
    [TestCase("Гараар ганц хүнийг дийлэх. Tолгойгоор мянган хүнийг дийлэх.", TestName="{m}(Mongolian-Proverb4)")]
    [TestCase("Աղւէսը բերանը խաղողին չի հասնում, ասում է՝ խակ է", TestName="{m}(Armenian-Proverb1)")]
    [TestCase("Ամեն փայտ շերեփ չի դառնա, ամեն սար՝ Մասիս", TestName="{m}(Armenian-Proverb2)")]
    [TestCase("Արևին ասում է դուրս մի արի՝ ես դուրս եմ եկել", TestName="{m}(Armenian-Proverb3)")]
    [TestCase("Գայլի գլխին Աւետարան են կարդում, ասում է՝ շուտ արէ՛ք, գալլէս գնաց", TestName="{m}(Armenian-Proverb4)")]
    [TestCase("पृथिव्यां त्रीणी रत्नानि जलमन्नं सुभाषितम्।", TestName="{m}(Hindi-Proverb1)")]
    [TestCase("जननी जन्मभुमिस्छ स्वर्गादपि गरीयसि", TestName="{m}(Hindi-Proverb2)")]
    [TestCase("न अभिशेको न संस्कारः सिम्हस्य कृयते वनेविक्रमार्जितसत्वस्य स्वयमेव मृगेन्द्रता", TestName="{m}(Hindi-Proverb3)")]
    public void EntityName_Works(String value)
    {
        EntityName s = new EntityName();
        s.SetString(value);

        String finalValue;
        //for truncation. The underlying implementaiton has the non .NET equivalent, but this should be a oracle to compare to
        var utf8 = new UTF8Encoding();
        var encoder = utf8.GetEncoder();
        unsafe
        {
            fixed (char* chars = value)
            {
                byte[] finalBytes = new byte[FixedString64Bytes.utf8MaxLengthInBytes];
                int byteLen = -1;
                int charsUsed = -1;
                int bytesUsed = -1;
                bool completed = false;
                fixed (byte* bytes = finalBytes)
                {
                    encoder.Convert(chars, value.Length, bytes, FixedString64Bytes.utf8MaxLengthInBytes,
                        true, out charsUsed, out bytesUsed, out completed);
                    byteLen = bytesUsed;
                }

                finalValue = Encoding.Default.GetString(finalBytes,0,bytesUsed);
            }
        }

        Assert.AreEqual(finalValue, s.ToString());
    }


    [TestCase("red001")]
    [TestCase("orange2")]
    [TestCase("yellow001234")]
    [TestCase("green")]
    [TestCase("紅色002", TestName="{m}(Chinese-Red)")]
    [TestCase("橙色0035", TestName="{m}(Chinese-Orange)")]
    [TestCase("黄色000234", TestName="{m}(Chinese-Yellow)")]
    [TestCase("绿色00003423", TestName="{m}(Chinese-Green)")]
    public void EntityName_FixedString32Works(String value)
    {
        EntityName w = new EntityName();
        FixedString32Bytes fixedString = default;
        w.SetString(value);
        w.ToFixedString(ref fixedString);
        Assert.AreEqual(value, fixedString.ToString());
    }

    [TestCase("red001")]
    [TestCase("orange2")]
    [TestCase("yellow001234")]
    [TestCase("green")]
    [TestCase("紅色002", TestName="{m}(Chinese-Red)")]
    [TestCase("橙色0035", TestName="{m}(Chinese-Orange)")]
    [TestCase("黄色000234", TestName="{m}(Chinese-Yellow)")]
    [TestCase("绿色00003423", TestName="{m}(Chinese-Green)")]
    public void EntityName_FixedString64Works(String value)
    {
        EntityName w = new EntityName();
        FixedString64Bytes fixedString = default;
        w.SetString(value);
        w.ToFixedString(ref fixedString);
        Assert.AreEqual(value, fixedString.ToString());
    }

    [TestCase("red001")]
    [TestCase("orange2")]
    [TestCase("yellow001234")]
    [TestCase("green")]
    [TestCase("紅色002", TestName="{m}(Chinese-Red)")]
    [TestCase("橙色0035", TestName="{m}(Chinese-Orange)")]
    [TestCase("黄色000234", TestName="{m}(Chinese-Yellow)")]
    [TestCase("绿色00003423", TestName="{m}(Chinese-Green)")]
    public void EntityName_FixedString128Works(String value)
    {
        EntityName w = new EntityName();
        FixedString128Bytes fixedString = default;
        w.SetString(value);
        w.ToFixedString(ref fixedString);
        Assert.AreEqual(value, fixedString.ToString());
    }
}

}
#endif

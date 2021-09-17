using System.IO;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Entities.Tests;
using Unity.Collections.NotBurstCompatible;

namespace Unity.Scenes.Tests
{
    public class SerializeUtilityDeterminism
    {
        public enum Mode
        {
            Yaml,
            YamlDumpChunk,
            Binary
        }
        static byte[] Write(EntityManager manager, Mode mode)
        {
            if (mode == Mode.Binary)
            {
                using (var writer = new TestBinaryWriter(manager.World.UpdateAllocator.ToAllocator))
                {
                    SerializeUtility.SerializeWorld(manager, writer);
                    return writer.content.ToArrayNBC();
                }
            }
            else
            {
                // Save the World to a memory buffer via a a Stream Writer
                using (var memStream = new MemoryStream())
                using (var sw = new StreamWriter(memStream))
                {
                    sw.NewLine = "\n";
                    if (mode == Mode.Yaml)
                        SerializeUtility.SerializeWorldIntoYAML(manager, sw, false);
                    else if (mode == Mode.YamlDumpChunk)
                        SerializeUtility.SerializeWorldIntoYAML(manager, sw, true);

                    sw.Flush();
                    memStream.Seek(0, SeekOrigin.Begin);
                    return memStream.ToArray();
                }
            }
        }

        [Test]
        public void DeterministicWrite([Values]Mode mode, [Values(0, 20)]int addBuffercount, [Values(1, 20)]int size)
        {
            using (var world0 = new World("1"))
            using (var world1 = new World("2"))
            {
                FillBuffer(addBuffercount, size, 100, world0);
                FillIComponentData(10, world0);
                var result0= Write(world0.EntityManager, mode);

                FillBuffer(addBuffercount, size, 200, world1);
                FillIComponentData(10, world1);
                var result1= Write(world1.EntityManager, mode);

                // Enable for debugging purposes (Diff these two files to see what is indeterministic if the test fails)
                //File.WriteAllBytes("test-src.txt", result0);
                //File.WriteAllBytes("test-dst.txt", result1);

                Assert.AreEqual(result0, result1);
            }
        }

        // Fill buffer with interministic values then resize and fill with deterministic values
        // Making sure that we don't write buffer data that is no longer in use.
        static void FillBuffer(int addBufferCount, int size, int fillvalue, World world)
        {
            var entity = world.EntityManager.CreateEntity();
            var buffer = world.EntityManager.AddBuffer<EcsIntElement>(entity);
            for (int i = 0; i != addBufferCount; i++)
                buffer.Add(fillvalue);
            buffer.ResizeUninitialized(size);
            for (int i = 0; i != size; i++)
                buffer[i] = i;
        }

        // Create entity with indeterministic value and destroy it again
        // Making sure that we don't write chunk data that has been released / not yet been cleared
        static void FillIComponentData(int fillvalue, World world0)
        {
            var entity = world0.EntityManager.CreateEntity();
            world0.EntityManager.AddComponentData(entity, new EcsTestData(10));

            var fillEntity = world0.EntityManager.CreateEntity();
            world0.EntityManager.AddComponentData(entity, new EcsTestData(fillvalue));
            world0.EntityManager.DestroyEntity(fillEntity);
        }
    }
}

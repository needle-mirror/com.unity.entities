using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{    
    [BurstCompile]
    [BurstCompatible]
    public unsafe struct Scratchpad 
    {    
        [BurstCompile]
        [BurstCompatible]
        [StructLayout(LayoutKind.Sequential, Size = 32768)]
        internal struct Shard 
        {
            internal AllocatorManager.AllocatorHandle m_handle;
            internal int m_next;
            
            public int Try(ref AllocatorManager.Block block)
            {
                if(block.Range.Pointer == IntPtr.Zero)
                {
                    var next = m_next;
                    var mask = JobsUtility.CacheLineSize - 1;
                    var bytes = (block.Bytes + mask) & ~mask;
                    if(m_next + bytes > 32768)
                        return -1;
                    m_next += (int)bytes; 
                    fixed(Shard* s = &this)
                        block.Range.Pointer = (IntPtr)((byte*)s + next);
                    return 0;
                }
                if(block.Range.Items == 0) // "Free" should be a no-op
                    return 0; // we could check to see if the pointer belongs to us, if we want to be strict about it.
                return -1;
            }

            [BurstCompile]
		    [MonoPInvokeCallback(typeof(AllocatorManager.TryFunction))]
            internal static int Try(IntPtr state, ref AllocatorManager.Block block)
            {
                return ((Shard*)state)->Try(ref block);
            }

            public void Rewind()
            {
                m_handle.Rewind();
                m_next = 16;
            }

        };
        Shard *m_shard;
        int m_shards;
        internal Shard* GetShard(int threadIndex)
        {
            return m_shard + threadIndex;
        }

        [NotBurstCompatible]
        public Scratchpad(int shards)
        {
            this = default;
            m_shards = shards;
            m_shard = (Shard*)Memory.Unmanaged.Allocate(sizeof(Shard) * m_shards, JobsUtility.CacheLineSize, Allocator.Persistent);
            var functionPointer = BurstCompiler.CompileFunctionPointer<AllocatorManager.TryFunction>(Shard.Try);
            for(var i = 0; i < m_shards; ++i)
            {
                m_shard[i].m_handle = AllocatorManager.Register((IntPtr)(m_shard + i), functionPointer);
                AllocatorManager.Managed.RegisterDelegate(m_shard[i].m_handle.Index, Shard.Try);
                m_shard[i].m_next = 16;
            }
        }

        [NotBurstCompatible]
        public void Dispose()
        {
            if(JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You cannot Dispose a Scratchpad from a Job.");
            for(var i = 0; i < m_shards; ++i)
                AllocatorManager.Unregister(ref m_shard[i].m_handle);
            Memory.Unmanaged.Free(m_shard, Allocator.Persistent);
        }

        public void Rewind()
        {
            if(JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You cannot Rewind a whole Scratchpad from a Job.");
            for(var shard = 0; shard < m_shards; ++shard)
                m_shard[shard].Rewind();
        }
    }
    
    [BurstCompile]
    [BurstCompatible]
    public unsafe struct ScratchpadAllocator : AllocatorManager.IAllocator
    {        
        [NativeDisableUnsafePtrRestriction]
        internal Scratchpad.Shard* m_Shard;

        [NativeSetThreadIndex]
        internal int m_ThreadIndex;            

        public ScratchpadAllocator(Scratchpad scratchpad)
        {
            m_Shard = scratchpad.GetShard(0);
            m_ThreadIndex = 0;
        }
        
        public void Dispose()
        {
        }
        
        [NotBurstCompatible]
        public AllocatorManager.TryFunction Function => null;

        [NotBurstCompatible]
        public int Try(ref AllocatorManager.Block block)
        {
            return m_Shard[m_ThreadIndex].Try(ref block);
        }
        
        public AllocatorManager.AllocatorHandle Handle 
        { 
            get 
            { 
                return m_Shard[m_ThreadIndex].m_handle; 
            }
            set 
            { 
                m_Shard[m_ThreadIndex].m_handle = value; 
            } 
        }

        public Allocator ToAllocator
        {
            get
            {
                return m_Shard[m_ThreadIndex].m_handle.ToAllocator;
            }
        }

        public bool IsCustomAllocator
        {
            get
            {
                return m_Shard[m_ThreadIndex].m_handle.IsCustomAllocator;
            }
        }


        public void Rewind()
        {
            m_Shard[m_ThreadIndex].Rewind();
        }
        
        /// <summary>
        /// Allocate a NativeArray of type T from memory that is guaranteed to remain valid until the Scratchpad
        /// allocator is Rewound (under user control). There is no contention for this memory between threads.
        /// There is no need to Dispose the NativeArray so allocated. It is not possible
        /// to free the memory by Disposing it - it is automatically freed when the Scratchpad is Rewound.
        /// </summary>               
        [BurstCompatible(GenericTypeArguments = new [] { typeof(int) })]
        public NativeArray<T> AllocateNativeArray<T>(int length) where T : unmanaged
        {
            var array = new NativeArray<T>();
            array.m_Buffer = this.Allocate(default(T), length);
            array.m_Length = length;
            array.m_AllocatorLabel = ToAllocator;
#if ENABLE_UNITY_COLLECTIONS_CHECKS            
            array.m_MinIndex = 0;
            array.m_MaxIndex = length - 1;
            array.m_Safety = AtomicSafetyHandle.Create();
            array.m_DisposeSentinel = null;            
            Handle.AddSafetyHandle(array.m_Safety);            
#endif
            return array;
        }
                
        /// <summary>
        /// Allocate a NativeList of type T from memory that is guaranteed to remain valid until the Scratchpad
        /// allocator is Rewound (under user control). There is no contention for this memory between threads.
        /// There is no need to Dispose the NativeList so allocated. It is not possible
        /// to free the memory by Disposing it - it is automatically freed when the Scratchpad is Rewound.
        /// </summary>               
        [BurstCompatible(GenericTypeArguments = new [] { typeof(int) })]
        public NativeList<T> AllocateNativeList<T>(int capacity) where T : unmanaged
        {
            unsafe
            {
                var container = new NativeList<T>();
                container.m_ListData = this.Allocate(default(UnsafeList<T>), 1);
                container.m_ListData->Ptr = this.Allocate(default(T), capacity);
                container.m_ListData->m_capacity = capacity;
                container.m_ListData->m_length = 0;
                container.m_ListData->Allocator = Handle;
                container.m_DeprecatedAllocator = ToAllocator;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                container.m_Safety = CollectionHelper.CreateSafetyHandle(ToAllocator);
#if REMOVE_DISPOSE_SENTINEL
#else
                container.m_DisposeSentinel = null;
#endif
                CollectionHelper.SetStaticSafetyId<NativeList<T>>(ref container.m_Safety, ref NativeList<T>.s_staticSafetyId.Data);
                AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(container.m_Safety, true);
                Handle.AddSafetyHandle(container.m_Safety);
#endif
                return container;
            }
        }

    }
}

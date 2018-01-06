﻿using BepuUtilities.Collections;
using BepuUtilities.Memory;
using System.Runtime.CompilerServices;

namespace BepuPhysics
{
    public struct ConstraintSet
    {
        public QuickList<ConstraintBatch, Buffer<ConstraintBatch>> Batches;

        public ConstraintSet(BufferPool pool, int initialBatchCapacity)
        {
            QuickList<ConstraintBatch, Buffer<ConstraintBatch>>.Create(pool.SpecializeFor<ConstraintBatch>(), initialBatchCapacity, out Batches);
        }

        /// <summary>
        /// Gets the total number of bundles across all types and batches.
        /// </summary>
        public int BundleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Batches.Count; ++i)
                {
                    ref var batch = ref Batches[i];
                    for (int j = 0; j < batch.TypeBatches.Count; ++j)
                    {
                        count += batch.TypeBatches[j].BundleCount;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Gets the total number of bundles across all types and batches.
        /// </summary>
        public int ConstraintCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Batches.Count; ++i)
                {
                    ref var batch = ref Batches[i];
                    for (int j = 0; j < batch.TypeBatches.Count; ++j)
                    {
                        count += batch.TypeBatches[j].BundleCount;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Gets whether this constraint set is allocated. 
        /// </summary>
        public bool Allocated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Batches.Span.Allocated; }
        }

        public void Clear(BufferPool pool)
        {
            for (int i = 0; i < Batches.Count; ++i)
            {
                Batches[i].Dispose(pool);
            }
            Batches.Count = 0;
        }

        public void Dispose(BufferPool pool)
        {
            for (int i = 0; i < Batches.Count; ++i)
            {
                Batches[i].Dispose(pool);
            }
            Batches.Dispose(pool.SpecializeFor<ConstraintBatch>());
            this = new ConstraintSet();
        }
    }
}
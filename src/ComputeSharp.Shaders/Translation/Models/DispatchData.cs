﻿using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ComputeSharp.Graphics.Buffers.Abstract;
using TerraFX.Interop;

namespace ComputeSharp.Shaders.Translation.Models
{
    /// <summary>
    /// A <see langword="struct"/> that contains all the captured data to dispatch a shader.
    /// </summary>
    internal readonly ref struct DispatchData
    {
        /// <summary>
        /// The <see cref="D3D12_GPU_DESCRIPTOR_HANDLE"/> array with the captured buffers.
        /// </summary>
        private readonly D3D12_GPU_DESCRIPTOR_HANDLE[] resourcesArray;

        /// <summary>
        /// The number of <see cref="GraphicsResource"/> instances in <see cref="resourcesArray"/>.
        /// </summary>
        private readonly int resourcesCount;

        /// <summary>
        /// The <see cref="byte"/> array with all the captured variables, with proper padding.
        /// </summary>
        private readonly byte[] variablesArray;

        /// <summary>
        /// The actual size in bytes to use from <see cref="variablesArray"/>.
        /// </summary>
        private readonly int variablesByteSize;

        /// <summary>
        /// Creates a new <see cref="DispatchData"/> instance with the specified parameters.
        /// </summary>
        /// <param name="resourcesArray">The <see cref="D3D12_GPU_DESCRIPTOR_HANDLE"/> array with the captured buffers.</param>
        /// <param name="resourcesCount">The number of <see cref="D3D12_GPU_DESCRIPTOR_HANDLE"/> instances in <see cref="resourcesArray"/>.</param>
        /// <param name="variablesArray">The <see cref="byte"/> array with all the captured variables, with proper padding.</param>
        /// <param name="variablesByteSize">The actual size in bytes to use from <see cref="variablesArray"/>.</param>
        public DispatchData(D3D12_GPU_DESCRIPTOR_HANDLE[] resourcesArray, int resourcesCount, byte[] variablesArray, int variablesByteSize)
        {
            this.resourcesArray = resourcesArray;
            this.variablesArray = variablesArray;
            this.resourcesCount = resourcesCount;
            this.variablesByteSize = variablesByteSize;
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> with all the captured buffers.
        /// </summary>
        public ReadOnlySpan<D3D12_GPU_DESCRIPTOR_HANDLE> Resources
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref D3D12_GPU_DESCRIPTOR_HANDLE r0 = ref MemoryMarshal.GetArrayDataReference(this.resourcesArray);

                return MemoryMarshal.CreateReadOnlySpan(ref r0, this.resourcesCount);
            }
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> with the padded data representing all the captured variables.
        /// </summary>
        public ReadOnlySpan<Int4> Variables
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref byte r0 = ref MemoryMarshal.GetArrayDataReference(this.variablesArray);
                ref Int4 r1 = ref Unsafe.As<byte, Int4>(ref r0);
                bool mod = (this.variablesByteSize & 15) > 0;
                int length = variablesByteSize / 4 + Unsafe.As<bool, byte>(ref mod);

                return MemoryMarshal.CreateReadOnlySpan(ref r1, length);
            }
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        public void Dispose()
        {
            ArrayPool<D3D12_GPU_DESCRIPTOR_HANDLE>.Shared.Return(this.resourcesArray);
            ArrayPool<byte>.Shared.Return(this.variablesArray);
        }
    }
}

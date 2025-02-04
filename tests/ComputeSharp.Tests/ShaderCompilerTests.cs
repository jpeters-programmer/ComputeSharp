using System;
using ComputeSharp;
using ComputeSharp.Descriptors;
using ComputeSharp.Interop;
using ComputeSharp.Tests.Misc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

#pragma warning disable IDE0008, IDE0022, IDE0009

namespace ComputeSharp.Tests
{
    [TestClass]
    public partial class ShaderCompilerTests
    {
        [TestMethod]
        public void ReflectionBytecode()
        {
            static ReadOnlyMemory<byte> GetHlslBytecode<T>()
            where T : struct, IComputeShaderDescriptor<T>
            {
                return T.HlslBytecode;
            }

            ShaderInfo shaderInfo = ReflectionServices.GetShaderInfo<ReservedKeywordsShader>();

            CollectionAssert.AreEqual(GetHlslBytecode<ReservedKeywordsShader>().ToArray(), shaderInfo.HlslBytecode.ToArray());
        }

        [TestMethod]
        public void ReservedKeywords()
        {
            _ = ReflectionServices.GetShaderInfo<ReservedKeywordsShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct ReservedKeywordsShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> row_major;
            public readonly float dword;
            public readonly float float2;
            public readonly int int2x2;

            public void Execute()
            {
                float exp = Hlsl.Exp(dword * row_major[ThreadIds.X]);
                float log = Hlsl.Log(1 + exp);

                row_major[ThreadIds.X] = (log / dword) + float2 + int2x2;
            }
        }

        [TestMethod]
        public void ReservedKeywordsInCustomTypes()
        {
            _ = ReflectionServices.GetShaderInfo<ReservedKeywordsInCustomTypesShader>();
        }

        public struct CellData
        {
            public float testX;
            public float testY;
            public uint seed;

            public float distance;
            public readonly float dword;
            public readonly float float2;
            public readonly int int2x2;
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct ReservedKeywordsInCustomTypesShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> row_major;
            public readonly CellData cellData;
            public readonly float dword;
            public readonly float float2;
            public readonly int int2x2;
            public readonly CellData cbuffer;

            public void Execute()
            {
                float exp = Hlsl.Exp(cellData.distance * row_major[ThreadIds.X]);
                float log = Hlsl.Log(1 + exp);
                float temp = log + cellData.dword + cellData.int2x2;

                row_major[ThreadIds.X] = (log / dword) + float2 + int2x2 + cbuffer.float2 + temp;
            }
        }

        // See https://github.com/Sergio0694/ComputeSharp/issues/313
        [TestMethod]
        public void ReservedKeywordsFromHlslTypesAndBuiltInValues()
        {
            _ = ReflectionServices.GetShaderInfo<ReservedKeywordsFromHlslTypesAndBuiltInValuesShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct ReservedKeywordsFromHlslTypesAndBuiltInValuesShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> fragmentKeyword;
            public readonly ReadWriteBuffer<float> compile_fragment;
            public readonly ReadWriteBuffer<float> shaderProfile;
            public readonly ReadWriteBuffer<float> maxvertexcount;
            public readonly ReadWriteBuffer<float> TriangleStream;
            public readonly ReadWriteBuffer<float> Buffer;
            public readonly ReadWriteBuffer<float> ByteAddressBuffer;
            public readonly int ConsumeStructuredBuffer;
            public readonly int RWTexture2D;
            public readonly int Texture2D;
            public readonly int Texture2DArray;
            public readonly int SV_DomainLocation;
            public readonly int SV_GroupIndex;
            public readonly int SV_OutputControlPointID;
            public readonly int SV_StencilRef;

            public void Execute()
            {
                float sum = ConsumeStructuredBuffer + RWTexture2D + Texture2D + Texture2DArray;

                sum += SV_DomainLocation + SV_GroupIndex + SV_OutputControlPointID + SV_StencilRef;

                fragmentKeyword[ThreadIds.X] = sum;
                compile_fragment[ThreadIds.X] = sum;
                shaderProfile[ThreadIds.X] = sum;
                maxvertexcount[ThreadIds.X] = sum;
                TriangleStream[ThreadIds.X] = sum;
                Buffer[ThreadIds.X] = sum;
                ByteAddressBuffer[ThreadIds.X] = sum;
            }
        }

        [TestMethod]
        public void ReservedKeywordsPrecompiled()
        {
            _ = ReflectionServices.GetShaderInfo<ReservedKeywordsPrecompiledShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct ReservedKeywordsPrecompiledShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> row_major;
            public readonly float dword;
            public readonly float float2;
            public readonly int int2x2;
            private readonly float sin;
            private readonly float cos;
            private readonly float scale;
            private readonly float intensity;

            public void Execute()
            {
                float exp = Hlsl.Exp(dword * row_major[ThreadIds.X]);
                float log = Hlsl.Log(1 + exp);

                float s1 = this.cos * exp * this.sin * log;
                float t1 = -this.sin * exp * this.cos * log;

                float s2 = s1 + this.intensity + Hlsl.Tan(s1 * this.scale);
                float t2 = t1 + this.intensity + Hlsl.Tan(t1 * this.scale);

                float u2 = (this.cos * s2) - (this.sin * t2);
                float v2 = (this.sin * s2) - (this.cos * t2);

                row_major[ThreadIds.X] = (log / dword) + float2 + int2x2 + u2 + v2;
            }
        }

        [TestMethod]
        public void SpecialTypeAsReturnType()
        {
            _ = ReflectionServices.GetShaderInfo<SpecialTypeAsReturnTypeShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct SpecialTypeAsReturnTypeShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float2> buffer;

            float2 Foo(float x) => x;

            public void Execute()
            {
                static float3 Bar(float x) => x;

                buffer[ThreadIds.X] = Foo(ThreadIds.X) + Bar(ThreadIds.X).XY;
            }
        }

        [TestMethod]
        public void LocalFunctionInExternalMethods()
        {
            _ = ReflectionServices.GetShaderInfo<LocalFunctionInExternalMethodsShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct LocalFunctionInExternalMethodsShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float2> buffer;

            float2 Foo(float x)
            {
                static float2 Baz(float y) => y;

                return Baz(x);
            }

            public void Execute()
            {
                buffer[ThreadIds.X] = Foo(ThreadIds.X);
            }
        }

        [TestMethod]
        public void CapturedNestedStructType()
        {
            _ = ReflectionServices.GetShaderInfo<CapturedNestedStructTypeShader>();
        }

        [AutoConstructor]
        public readonly partial struct CustomStructType
        {
            public readonly float2 a;
            public readonly int b;
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct CapturedNestedStructTypeShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;
            public readonly CustomStructType foo;

            /// <inheritdoc/>
            public void Execute()
            {
                buffer[ThreadIds.X] *= foo.a.X + foo.b;
            }
        }

        [TestMethod]
        public void ExternalStructType_Ok()
        {
            _ = ReflectionServices.GetShaderInfo<ExternalStructTypeShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct ExternalStructTypeShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            /// <inheritdoc/>
            public void Execute()
            {
                float value = buffer[ThreadIds.X];
                ExternalStructType type = ExternalStructType.New((int)value, Hlsl.Abs(value));

                buffer[ThreadIds.X] = ExternalStructType.Sum(type);
            }
        }

        [TestMethod]
        public void OutOfOrderMethods()
        {
            _ = ReflectionServices.GetShaderInfo<OutOfOrderMethodsShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct OutOfOrderMethodsShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            static int A() => B();

            static int B() => 1 + C();

            static int C() => 1;

            public int D() => A() + E() + F();

            int E() => 1;

            static int F() => 1;

            /// <inheritdoc/>
            public void Execute()
            {
                float value = buffer[ThreadIds.X];
                ExternalStructType type = ExternalStructType.New((int)value, Hlsl.Abs(value));

                buffer[ThreadIds.X] = ExternalStructType.Sum(type);
            }
        }

        [TestMethod]
        public void PixelShader()
        {
            ShaderInfo info = ReflectionServices.GetShaderInfo<StatelessPixelShader, float4>();

            Assert.AreEqual(info.TextureStoreInstructionCount, 1u);
            Assert.AreEqual(info.BoundResourceCount, 2u);
        }

        [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct StatelessPixelShader : IComputeShader<float4>
        {
            /// <inheritdoc/>
            public float4 Execute()
            {
                return new(1, 1, 1, 1);
            }
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct LoopWithVarCounterShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            /// <inheritdoc/>
            public void Execute()
            {
                for (var i = 0; i < 10; i++)
                {
                    buffer[(ThreadIds.X * 10) + i] = i;
                }
            }
        }

        [TestMethod]
        public void LoopWithVarCounter()
        {
            _ = ReflectionServices.GetShaderInfo<LoopWithVarCounterShader>();
        }

        [TestMethod]
        public void DoublePrecisionSupport()
        {
            ShaderInfo info = ReflectionServices.GetShaderInfo<DoublePrecisionSupportShader>();

            Assert.IsTrue(info.RequiresDoublePrecisionSupport);
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct DoublePrecisionSupportShader : IComputeShader
        {
            public readonly ReadWriteBuffer<double> buffer;
            public readonly double factor;

            /// <inheritdoc/>
            public void Execute()
            {
                buffer[ThreadIds.X] *= factor + 3.14;
            }
        }

        [TestMethod]
        public void FieldAccessWithThisExpression()
        {
            _ = ReflectionServices.GetShaderInfo<FieldAccessWithThisExpressionShader>();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct FieldAccessWithThisExpressionShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float number;

            /// <inheritdoc/>
            public void Execute()
            {
                this.buffer[ThreadIds.X] *= this.number;
            }
        }

        [TestMethod]
        public void ComputeShaderWithInheritedShaderInterface()
        {
            _ = ReflectionServices.GetShaderInfo<ComputeShaderWithInheritedShaderInterfaceShader>();
        }

        public interface IMyBaseShader : IComputeShader
        {
            public int A { get; }

            public void B();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct ComputeShaderWithInheritedShaderInterfaceShader : IMyBaseShader
        {
            int IMyBaseShader.A => 42;

            void IMyBaseShader.B()
            {
            }

            public readonly ReadWriteBuffer<float> buffer;
            public readonly float number;

            /// <inheritdoc/>
            public void Execute()
            {
                this.buffer[ThreadIds.X] *= this.number;
            }
        }

        [TestMethod]
        public void PixelShaderWithInheritedShaderInterface()
        {
            _ = ReflectionServices.GetShaderInfo<PixelShaderWithInheritedShaderInterfaceShader, float4>();
        }

        public interface IMyBaseShader<T> : IComputeShader<T>
            where T : unmanaged
        {
            public int A { get; }

            public void B();
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct PixelShaderWithInheritedShaderInterfaceShader : IMyBaseShader<float4>
        {
            int IMyBaseShader<float4>.A => 42;

            void IMyBaseShader<float4>.B()
            {
            }

            public readonly float number;

            /// <inheritdoc/>
            public float4 Execute()
            {
                return default;
            }
        }

        [TestMethod]
        public void StructInstanceMethods()
        {
            _ = ReflectionServices.GetShaderInfo<StructInstanceMethodsShader>();
        }

        public struct MyStructTypeA
        {
            public int A;
            public float B;

            public float Sum()
            {
                return A + Bar();
            }

            public float Bar() => this.B;
        }

        public struct MyStructTypeB
        {
            public MyStructTypeA A;
            public float B;

            public float Combine()
            {
                return A.Sum() + this.B;
            }
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct StructInstanceMethodsShader : IComputeShader
        {
            public readonly MyStructTypeA a;
            public readonly MyStructTypeB b;
            public readonly ReadWriteBuffer<MyStructTypeA> bufferA;
            public readonly ReadWriteBuffer<MyStructTypeB> bufferB;
            public readonly ReadWriteBuffer<float> results;

            /// <inheritdoc/>
            public void Execute()
            {
                float result1 = a.Sum() + a.Bar();
                float result2 = b.Combine();

                results[ThreadIds.X] = result1 + result2 + bufferA[ThreadIds.X].Sum() + bufferB[0].Combine();
            }
        }

        [TestMethod]
        public void ComputeShaderWithScopedParameterInMethods()
        {
            _ = ReflectionServices.GetShaderInfo<ComputeShaderWithScopedParameterInMethodsShader>();
        }

        internal static class HelpersForComputeShaderWithScopedParameterInMethods
        {
            public static void Baz(scoped in float a, scoped ref float b)
            {
                b = a;
            }
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct ComputeShaderWithScopedParameterInMethodsShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float number;

            private static void Foo(scoped ref float a, ref float b)
            {
                b = a;
            }

            private void Bar(scoped ref float a, scoped ref float b)
            {
                b = a;
            }

            /// <inheritdoc/>
            public void Execute()
            {
                float x = this.number + ThreadIds.X;

                Foo(ref this.buffer[ThreadIds.X], ref x);
                Bar(ref this.buffer[ThreadIds.X], ref x);
                HelpersForComputeShaderWithScopedParameterInMethods.Baz(in this.buffer[ThreadIds.X], ref x);

                this.buffer[ThreadIds.X] *= x;
            }
        }

        [TestMethod]
        public void ShaderWithStrippedReflectionData()
        {
            ShaderInfo info1 = ReflectionServices.GetShaderInfo<ShaderWithStrippedReflectionData1>();

            // With no reflection data available, the instruction count is just 0
            Assert.AreEqual(0u, info1.InstructionCount);

            ShaderInfo info2 = ReflectionServices.GetShaderInfo<ShaderWithStrippedReflectionData2>();

            // Sanity check, here instead we should have some valid count
            Assert.AreNotEqual(0u, info2.InstructionCount);

            // Verify that the bytecode with stripped reflection is much smaller
            Assert.IsTrue(info1.HlslBytecode.Length < 1800);
            Assert.IsTrue(info2.HlslBytecode.Length > 3300);
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [CompileOptions(CompileOptions.Default | CompileOptions.StripReflectionData)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct ShaderWithStrippedReflectionData1 : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            /// <inheritdoc/>
            public void Execute()
            {
                this.buffer[ThreadIds.X] = ThreadIds.X;
            }
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [CompileOptions(CompileOptions.Default)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct ShaderWithStrippedReflectionData2 : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            /// <inheritdoc/>
            public void Execute()
            {
                this.buffer[ThreadIds.X] = ThreadIds.X;
            }
        }

        [TestMethod]
        public void GloballyCoherentBuffers()
        {
            ShaderInfo info = ReflectionServices.GetShaderInfo<GloballyCoherentBufferShader>();

            Assert.AreEqual(
                """
                // ================================================
                //                  AUTO GENERATED
                // ================================================
                // This shader was created by ComputeSharp.
                // See: https://github.com/Sergio0694/ComputeSharp.

                #define __GroupSize__get_X 64
                #define __GroupSize__get_Y 1
                #define __GroupSize__get_Z 1

                cbuffer _ : register(b0)
                {
                    uint __x;
                    uint __y;
                    uint __z;
                }

                globallycoherent RWStructuredBuffer<int> __reserved__buffer : register(u0);

                [NumThreads(__GroupSize__get_X, __GroupSize__get_Y, __GroupSize__get_Z)]
                void Execute(uint3 ThreadIds : SV_DispatchThreadID)
                {
                    if (ThreadIds.x < __x && ThreadIds.y < __y && ThreadIds.z < __z)
                    {
                        InterlockedAdd(__reserved__buffer[0], 1);
                    }
                }
                """,
                info.HlslSource);
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct GloballyCoherentBufferShader : IComputeShader
        {
            [GloballyCoherent]
            private readonly ReadWriteBuffer<int> buffer;

            public void Execute()
            {
                Hlsl.InterlockedAdd(ref this.buffer[0], 1);
            }
        }

        [TestMethod]
        public void ComputeShaderWithRefReadonlyParameterInMethods()
        {
            _ = ReflectionServices.GetShaderInfo<ComputeShaderWithRefReadonlyParameterInMethodsShader>();
        }

        internal static class HelpersForCommputeShaderWithRefReadonlyParameterInMethods
        {
            public static float Baz(ref readonly float a, scoped ref readonly float b)
            {
                return a + b;
            }
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct ComputeShaderWithRefReadonlyParameterInMethodsShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float number;

            private static float Foo(ref readonly float a, scoped ref readonly float b)
            {
                return a + b;
            }

            private float Bar(ref readonly float a, scoped ref readonly float b)
            {
                return a + b;
            }

            /// <inheritdoc/>
            public void Execute()
            {
                float x = this.number + ThreadIds.X;

                x += Foo(ref x, in x);
                x += Foo(in this.number, in this.number);
                x += Bar(ref x, in x);
                x += HelpersForCommputeShaderWithRefReadonlyParameterInMethods.Baz(in this.buffer[ThreadIds.X], ref x);

                this.buffer[ThreadIds.X] = x;
            }
        }

        [TestMethod]
        public void AllRefTypesShader_RewritesRefParametersCorrectly()
        {
            ShaderInfo info = ReflectionServices.GetShaderInfo<AllRefTypesShader>();

            Assert.AreEqual(
                """
                // ================================================
                //                  AUTO GENERATED
                // ================================================
                // This shader was created by ComputeSharp.
                // See: https://github.com/Sergio0694/ComputeSharp.

                #define __GroupSize__get_X 64
                #define __GroupSize__get_Y 1
                #define __GroupSize__get_Z 1

                cbuffer _ : register(b0)
                {
                    uint __x;
                    uint __y;
                    uint __z;
                }

                RWStructuredBuffer<float> __reserved__buffer : register(u0);

                static void Foo(in int a, in int b, inout int c, out int d);

                static void Bar(in int a, in int b, inout int c, out int d);

                static void Foo(in int a, in int b, inout int c, out int d)
                {
                    d = 0;
                }

                static void Bar(in int a, in int b, inout int c, out int d)
                {
                    d = 0;
                }

                [NumThreads(__GroupSize__get_X, __GroupSize__get_Y, __GroupSize__get_Z)]
                void Execute(uint3 ThreadIds : SV_DispatchThreadID)
                {
                    if (ThreadIds.x < __x && ThreadIds.y < __y && ThreadIds.z < __z)
                    {
                    }
                }
                """,
                info.HlslSource);
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct AllRefTypesShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            public static void Foo(
                in int a,
                ref readonly int b,
                ref int c,
                out int d)
            {
                d = 0;
            }

            public static void Bar(
                scoped in int a,
                scoped ref readonly int b,
                scoped ref int c,
                scoped out int d)
            {
                d = 0;
            }

            /// <inheritdoc/>
            public void Execute()
            {
            }
        }

        [TestMethod]
        public void ShaderWithPartialDeclarations_IsCombinedCorrectly()
        {
            ShaderInfo info = ReflectionServices.GetShaderInfo<ShaderWithPartialDeclarations>();

            Assert.AreEqual(
                """
                // ================================================
                //                  AUTO GENERATED
                // ================================================
                // This shader was created by ComputeSharp.
                // See: https://github.com/Sergio0694/ComputeSharp.

                #define __GroupSize__get_X 64
                #define __GroupSize__get_Y 1
                #define __GroupSize__get_Z 1
                #define __ComputeSharp_Tests_ShaderCompilerTests_ShaderWithPartialDeclarations__a 2

                static const int b = 4;

                cbuffer _ : register(b0)
                {
                    uint __x;
                    uint __y;
                    uint __z;
                }

                RWStructuredBuffer<float> __reserved__buffer : register(u0);

                static int Sum(int x, int y);

                static int Sum(int x, int y)
                {
                    return x + y;
                }

                [NumThreads(__GroupSize__get_X, __GroupSize__get_Y, __GroupSize__get_Z)]
                void Execute(uint3 ThreadIds : SV_DispatchThreadID)
                {
                    if (ThreadIds.x < __x && ThreadIds.y < __y && ThreadIds.z < __z)
                    {
                        __reserved__buffer[ThreadIds.x] = Sum(__ComputeSharp_Tests_ShaderCompilerTests_ShaderWithPartialDeclarations__a, b);
                    }
                }
                """,
                info.HlslSource);
        }

        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        internal readonly partial struct ShaderWithPartialDeclarations : IComputeShader
        {
        }

        partial struct ShaderWithPartialDeclarations
        {
            public readonly ReadWriteBuffer<float> buffer;
        }

        partial struct ShaderWithPartialDeclarations
        {
            /// <inheritdoc/>
            public void Execute()
            {
                buffer[ThreadIds.X] = Sum(a, b);
            }
        }

        partial struct ShaderWithPartialDeclarations
        {
            private const int a = 2;
        }

        partial struct ShaderWithPartialDeclarations
        {
            private static readonly int b = 4;
        }

        partial struct ShaderWithPartialDeclarations
        {
            private static int Sum(int x, int y)
            {
                return x + y;
            }
        }
    }
}

namespace ExternalNamespace
{
    [TestClass]
    public partial class ShaderCompilerTestsInExternalNamespace
    {
        [AutoConstructor]
        [ThreadGroupSize(DefaultThreadGroupSizes.X)]
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct UserDefinedTypeShader : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;

            /// <inheritdoc/>
            public void Execute()
            {
                for (var i = 0; i < 10; i++)
                {
                    buffer[(ThreadIds.X * 10) + i] = i;
                }
            }
        }

        [TestMethod]
        public void UserDefinedType()
        {
            _ = ReflectionServices.GetShaderInfo<UserDefinedTypeShader>();
        }
    }
}
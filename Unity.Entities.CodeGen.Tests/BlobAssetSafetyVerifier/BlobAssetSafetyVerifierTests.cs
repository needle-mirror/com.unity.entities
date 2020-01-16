using System;
using System.Collections.Generic;
using Mono.Cecil;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.Entities.CodeGen.Tests
{
    [TestFixture]
    public class BlobAssetSafetyVerifierTests : PostProcessorTestBase
    {
        public struct MyBlob
        {
            public BlobArray<float> myfloats;
        }

        class StoreBlobAssetReferenceValueInLocal_Class
        {
            static BlobAssetReference<MyBlob> _blobAssetReference;
            
            void Method()
            {
                MyBlob blob = _blobAssetReference.Value;
                EnsureNotOptimizedAway(blob.myfloats.Length);
            }
        }

        [Test]
        public void StoreBlobAssetReferenceValueInLocal()
        {
            AssertProducesError(
                typeof(StoreBlobAssetReferenceValueInLocal_Class), 
                "error MayOnlyLiveInBlobStorageViolation: MyBlob may only live in blob storage. Access it by ref instead: `ref MyBlob yourVariable = ref ...`");
        }

        class LoadFieldFromBlobAssetReference_Class
        {
            static BlobAssetReference<MyBlob> _blobAssetReference;
            
            void Method()
            {
                BlobArray<float> myFloats = _blobAssetReference.Value.myfloats;
                EnsureNotOptimizedAway(myFloats.Length);
            }
        }

        [Test]
        public void LoadFieldFromBlobAssetReference()
        {
            AssertProducesError(
                typeof(LoadFieldFromBlobAssetReference_Class), 
                " error MayOnlyLiveInBlobStorageViolation: You may only access .myfloats by ref, as it may only live in blob storage. try `ref BlobArray<Single> yourVariable = ref yourMyBlob.myfloats`");
        }

        void AssertProducesError(Type typeWithCodeUnderTest, string shouldContain)
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeWithCodeUnderTest);
            var errors = new List<DiagnosticMessage>();

            try
            {
                BlobAssetSafetyVerifier.VerifyMethod(methodToAnalyze, new HashSet<TypeReference>());
            }
            catch (FoundErrorInUserCodeException exc)
            {
                errors.AddRange(exc.DiagnosticMessages);
            }

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(DiagnosticType.Error, errors[0].DiagnosticType);

            foreach(var error in errors)
                StringAssert.Contains(shouldContain, error.MessageData);
            
            AssertDiagnosticHasSufficientFileAndLineInfo(errors);
        }
    }
}
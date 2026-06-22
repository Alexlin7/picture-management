using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public sealed class CpuSessionFactory : IInferenceSessionFactory
{
    public InferenceBackend Backend => InferenceBackend.Cpu;

    public InferenceSession Create(string modelPath) => new(modelPath);   // 預設 CPU EP
}

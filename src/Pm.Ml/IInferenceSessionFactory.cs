using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public interface IInferenceSessionFactory
{
    InferenceBackend Backend { get; }
    InferenceSession Create(string modelPath);
}

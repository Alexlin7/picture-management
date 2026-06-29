#if INFER_DIRECTML
using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public sealed class DirectMlSessionFactory : IInferenceSessionFactory
{
    private readonly int _deviceId;
    public DirectMlSessionFactory(int deviceId = 0) => _deviceId = deviceId;

    public InferenceBackend Backend => InferenceBackend.DirectMl;

    public InferenceSession Create(string modelPath)
    {
        var so = new SessionOptions();
        so.AppendExecutionProvider_DML(_deviceId);
        return new InferenceSession(modelPath, so);
    }
}
#endif

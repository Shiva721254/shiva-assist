using System.Text.Json;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Windows.Repairs;

// ShivaAssistAgent: the elevated helper (architecture 7.3). Started by Shiva Assist with one Windows permission
// prompt, it runs exactly one approved job from the repair catalogue and exits. It never runs anything else.

if (args.Length != 1 || !args[0].EndsWith(".job.json", StringComparison.OrdinalIgnoreCase) || !File.Exists(args[0]))
{
    return 64;
}

var jobPath = Path.GetFullPath(args[0]);
RepairJob? job;
try
{
    job = RepairJson.Deserialize<RepairJob>(File.ReadAllText(jobPath));
}
catch (JsonException)
{
    return 65;
}

// The result may only be written next to the job file, with the matching name.
var expectedResult = jobPath[..^".job.json".Length] + ".result.json";
if (job is null || job.Schema != RepairJob.SchemaId || !RepairCatalogue.Contains(job.OperationId)
    || !string.Equals(Path.GetFullPath(job.ResultPath ?? string.Empty), expectedResult, StringComparison.OrdinalIgnoreCase))
{
    return 65;
}

RepairResult result;
try
{
    result = await new WindowsRepairExecutor(isElevated: true).ExecuteAsync(job, CancellationToken.None);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
{
    result = new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false, Detail = ex.GetType().Name + ": " + ex.Message };
}

File.WriteAllText(expectedResult, RepairJson.Serialize(result));
return result.Completed ? 0 : 1;

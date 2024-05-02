using Amazon.AutoScaling;
using Amazon.AutoScaling.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

Log.Information("ECSLypo starting up");

try
{

    bool enabled;
    bool shouldDecrementDesiredCapacity;
    ushort runningTask;
    int wait = 33;
    bool.TryParse(Environment.GetEnvironmentVariable("ECSLYPO_DECREMENT_ASG_CAPACITY"), out shouldDecrementDesiredCapacity);
    bool.TryParse(Environment.GetEnvironmentVariable("ECSLYPO_IS_ENABLED"), out enabled);

    if (ushort.TryParse(Environment.GetEnvironmentVariable("ECSLYPO_RUNNING_TASK"), out runningTask) == false)
    {
        Log.Logger.Warning("ECSLYPO_RUNNING_TASK variable undefined");
        return;
    }

    if (!enabled)
    {
        return;
    }

    string? selected_cluster = Environment.GetEnvironmentVariable("ECSLYPO_ECS_CLUSTER");
    if (string.IsNullOrEmpty(selected_cluster))
    {
        Log.Logger.Warning("ECSLYPO_ECS_CLUSTER variable undefined");
        return;
    }
    var ecs_client = new AmazonECSClient();
    var autoscaling_client = new AmazonAutoScalingClient();
    var describeClustersRequest = new DescribeContainerInstancesRequest
    {
        Cluster = selected_cluster
    };

    var describeContainerInstancesResponse = await ecs_client.DescribeContainerInstancesAsync(describeClustersRequest);

    var selected_container_instances = describeContainerInstancesResponse.ContainerInstances.Where(ci => ci.RunningTasksCount <= runningTask && ci.PendingTasksCount == 0);
    var selected_container_instances_chunks = SplitIntoChunks(selected_container_instances);
    foreach (var chunk in selected_container_instances_chunks)
    {
        var updateContainerInstancesStateRequest = new UpdateContainerInstancesStateRequest
        {
            Cluster = selected_cluster,
            ContainerInstances = chunk.Select(ci => ci.ContainerInstanceArn).ToList(),
            Status = "DRAINING"
        };
        await ecs_client.UpdateContainerInstancesStateAsync(updateContainerInstancesStateRequest);
        Log.Logger.Information($"Instances {string.Join(",", chunk.Select(ci => ci.Ec2InstanceId))} set to DRAINING");
    }

    var draining_instances = selected_container_instances.Count();
    var running_tasks = draining_instances + 1;
    var big_chunks = SplitIntoChunks(selected_container_instances, 100);
    while (running_tasks > draining_instances)
    {
        running_tasks = 0;

        foreach (var chunk in big_chunks)
        {
            var describeContainerInstancesRequest = new DescribeContainerInstancesRequest
            {
                Cluster = selected_cluster,
                ContainerInstances = chunk.Select(ci => ci.ContainerInstanceArn).ToList()
            };
            var dCIR = await ecs_client.DescribeContainerInstancesAsync(describeContainerInstancesRequest);
            running_tasks += dCIR.ContainerInstances.Select(ci => ci.RunningTasksCount).Sum();
        }
        Log.Logger.Information($"Running tasks {running_tasks} expected {draining_instances} waiting {wait} seconds.");
        System.Threading.Thread.Sleep(wait * 1000);
    }

    foreach (var ci in selected_container_instances)
    {
        var terminateInstanceInAutoScalingGroupRequest = new TerminateInstanceInAutoScalingGroupRequest
        {
            InstanceId = ci.Ec2InstanceId,
            ShouldDecrementDesiredCapacity = shouldDecrementDesiredCapacity
        };
        var terminateInstanceInAutoScalingGroupResponse = await autoscaling_client.TerminateInstanceInAutoScalingGroupAsync(terminateInstanceInAutoScalingGroupRequest);
        Log.Logger.Information($"Instance {ci.Ec2InstanceId} started termination with status {terminateInstanceInAutoScalingGroupResponse.Activity.StatusMessage}");
    }

}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
}
finally
{
    Log.Information("Shut down complete");
    Log.CloseAndFlush();
}


List<List<ContainerInstance>> SplitIntoChunks(IEnumerable<ContainerInstance> items, int chunkSize = 10)
{
    return items.Select((x, i) => new { Index = i, Value = x })
                  .GroupBy(x => x.Index / 10)
                  .Select(x => x.Select(v => v.Value).ToList())
                  .ToList();
}
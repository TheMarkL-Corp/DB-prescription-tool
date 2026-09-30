using Xunit;

// Disable parallel test execution across test classes to prevent filesystem race conditions 
// when tests rename or test embedded resource fallback of wwwroot.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

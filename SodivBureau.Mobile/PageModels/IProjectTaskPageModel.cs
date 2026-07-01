using CommunityToolkit.Mvvm.Input;
using SodivBureau.Mobile.Models;

namespace SodivBureau.Mobile.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}
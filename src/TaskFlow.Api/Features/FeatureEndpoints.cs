namespace TaskFlow.Api.Features;

public static class FeatureEndpoints
{
    public static void MapFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        Authentication.Login.LoginEndpoint.Map(app);
        Authentication.Register.RegisterEndpoint.Map(app);
        Authentication.Logout.LogoutEndpoint.Map(app);

        Users.GetProfile.GetProfileEndpoint.Map(app);
        Users.UpdateProfile.UpdateProfileEndpoint.Map(app);
        Users.ChangePassword.ChangePasswordEndpoint.Map(app);

        Workspaces.ListWorkspaces.ListWorkspacesEndpoint.Map(app);
        Workspaces.CreateWorkspace.CreateWorkspaceEndpoint.Map(app);
        Workspaces.Members.ListMembers.ListMembersEndpoint.Map(app);
        Workspaces.Members.AddMember.AddMemberEndpoint.Map(app);
        Workspaces.Members.ChangeMemberRole.ChangeMemberRoleEndpoint.Map(app);
        Workspaces.Members.RemoveMember.RemoveMemberEndpoint.Map(app);

        Boards.ListBoards.ListBoardsEndpoint.Map(app);
        Boards.CreateBoard.CreateBoardEndpoint.Map(app);
        Boards.GetBoard.GetBoardEndpoint.Map(app);
        Boards.UpdateBoard.UpdateBoardEndpoint.Map(app);
        Boards.ArchiveBoard.ArchiveBoardEndpoint.Map(app);

        Tasks.CreateTask.Endpoint.Map(app);
        Tasks.UpdateTask.UpdateTaskEndpoint.Map(app);
        Tasks.UpdateTaskStatus.UpdateTaskStatusEndpoint.Map(app);
        Tasks.DeleteTask.DeleteTaskEndpoint.Map(app);
        Reporting.GetBoardStats.GetBoardStatsEndpoint.Map(app);
        Activity.GetBoardActivity.GetBoardActivityEndpoint.Map(app);
    }
}

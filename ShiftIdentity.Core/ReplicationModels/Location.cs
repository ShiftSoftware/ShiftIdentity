namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public record Location(decimal[] coordinates, string type = "Point");
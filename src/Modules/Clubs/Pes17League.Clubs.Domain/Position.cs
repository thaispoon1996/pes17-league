namespace Pes17League.Clubs.Domain;

/// <summary>Vị trí đăng ký. Giá trị trùng mã lưu trong option file PES6 (1 không dùng).</summary>
public enum Position
{
    GK = 0, SW = 2, CB = 3, SB = 4, DMF = 5, WB = 6, CMF = 7, SMF = 8, AMF = 9, WF = 10, SS = 11, CF = 12,
}

public enum PositionGroup { Goalkeeper, Defender, Midfielder, Forward }

public static class PositionExtensions
{
    public static PositionGroup Group(this Position p) => p switch
    {
        Position.GK => PositionGroup.Goalkeeper,
        Position.SW or Position.CB or Position.SB or Position.WB => PositionGroup.Defender,
        Position.DMF or Position.CMF or Position.SMF or Position.AMF => PositionGroup.Midfielder,
        _ => PositionGroup.Forward,
    };
}

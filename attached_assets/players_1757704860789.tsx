import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RefreshCw, UsersRound, Search, Download } from "lucide-react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { queryClient } from "@/lib/queryClient";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useSelectedLeague } from "@/hooks/useSelectedLeague";

export default function Players() {
  const [userId] = useState("default-user");
  const selectedSport = "ffl";
  const { selectedLeagueId, leagues } = useSelectedLeague(userId);

  // Automatically use the current season from the selected league
  const selectedSeason = (() => {
    if (selectedLeagueId && Array.isArray(leagues)) {
      const league = leagues.find((l: any) => l.id === selectedLeagueId);
      return league?.season?.toString() || "2025";
    }
    return "2025";
  })();

  const [searchTerm, setSearchTerm] = useState<string>("");
  const [viewMode, setViewMode] = useState<"all" | "waiver">("all");
  const [selectedPosition, setSelectedPosition] = useState<string>("all");

  // Query players data
  const { data: playersData, isLoading: playersLoading } = useQuery({
    queryKey: ["/api/players", selectedSport, selectedSeason, userId],
    queryFn: async () => {
      const response = await fetch(`/api/players/${selectedSport}/${selectedSeason}?userId=${userId}`);
      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to fetch players: ${response.status} ${errorText}`);
      }
      return response.json();
    },
    enabled: !!selectedSeason && viewMode === "all",
  });

  // Query waiver wire data
  const { data: waiverWireData, isLoading: waiverWireLoading } = useQuery({
    queryKey: ["/api/leagues", selectedLeagueId, "waiver-wire"],
    enabled: !!selectedLeagueId && viewMode === "waiver",
  });

  const currentData = viewMode === "waiver" ? waiverWireData : playersData;
  const currentLoading = viewMode === "waiver" ? waiverWireLoading : playersLoading;

  // Helper function to get player name from various possible fields
  const getPlayerName = (playerData: any) => {
    // The actual player info is nested in playerData.player
    const player = playerData.player || playerData;
    
    if (player.fullName) return player.fullName;
    if (player.name) return player.name;
    if (player.displayName) return player.displayName;
    if (player.firstName && player.lastName) return `${player.firstName} ${player.lastName}`;
    
    return 'Unknown Player';
  };

  // Helper function to get position ID from various possible fields
  const getPlayerPositionId = (playerData: any) => {
    // The actual player info is nested in playerData.player
    const player = playerData.player || playerData;
    return player.defaultPositionId ?? player.positionId ?? player.position ?? 0;
  };

  // Helper function to get ownership percentage
  const getOwnershipPercent = (playerData: any) => {
    const player = playerData.player || playerData;
    return player.ownership?.percentOwned?.toFixed(1) || "0.0";
  };

  // Helper function to get pro team ID
  const getProTeamId = (playerData: any) => {
    const player = playerData.player || playerData;
    return player.proTeamId;
  };

  // Helper function to get injury status
  const getInjuryStatus = (playerData: any) => {
    const player = playerData.player || playerData;
    return player.injured || player.injuryStatus === 'INJURED' ? 'Injured' : 'Active';
  };

  // Helper function to get team name from pro team ID
  const getTeamName = (teamId: number) => {
    const teamNames: Record<number, string> = {
      1: "ATL", 2: "BUF", 3: "CHI", 4: "CIN", 5: "CLE", 6: "DAL", 7: "DEN", 8: "DET",
      9: "GB", 10: "TEN", 11: "IND", 12: "KC", 13: "LV", 14: "LAR", 15: "MIA", 16: "MIN",
      17: "NE", 18: "NO", 19: "NYG", 20: "NYJ", 21: "PHI", 22: "ARI", 23: "PIT", 24: "LAC",
      25: "SF", 26: "SEA", 27: "TB", 28: "WAS", 29: "CAR", 30: "JAX", 33: "BAL", 34: "HOU"
    };
    return teamNames[teamId] || `Team ${teamId}`;
  };

  // Helper function to get projected fantasy points
  const getProjectedPoints = (playerData: any) => {
    const player = playerData.player || playerData;
    // Check for projected stats in various possible locations
    const projection = player.stats?.find((stat: any) => stat.statSourceId === 1 && stat.statSplitTypeId === 1) ||
                      player.projectedStats ||
                      player.outlook?.projectedStats;
    
    if (projection?.appliedTotal !== undefined) {
      return projection.appliedTotal.toFixed(1);
    }
    if (projection?.total !== undefined) {
      return projection.total.toFixed(1);
    }
    return "-";
  };

  // Helper function to get opponent team
  const getOpponent = (playerData: any) => {
    const player = playerData.player || playerData;
    // Check for opponent info in various possible locations
    const opponent = player.opponent || 
                    player.nextOpponent ||
                    player.schedule?.find((game: any) => game.isThisWeek);
    
    if (opponent?.teamId) {
      return `vs ${getTeamName(opponent.teamId)}`;
    }
    if (opponent?.opponentTeamId) {
      return `vs ${getTeamName(opponent.opponentTeamId)}`;
    }
    return "-";
  };

  // Helper function to get game status/time
  const getGameStatus = (playerData: any) => {
    const player = playerData.player || playerData;
    const gameInfo = player.gameStatus || 
                    player.schedule?.find((game: any) => game.isThisWeek) ||
                    player.nextGame;
    
    if (gameInfo?.gameTime) {
      return new Date(gameInfo.gameTime).toLocaleDateString();
    }
    if (gameInfo?.status) {
      return gameInfo.status;
    }
    return "-";
  };

  const getPositionColor = (positionId: number) => {
    const colors: Record<number, string> = {
      0: "bg-chart-1", // QB
      1: "bg-chart-1", // QB
      2: "bg-chart-2", // RB
      3: "bg-chart-3", // WR
      4: "bg-chart-4", // TE
      5: "bg-chart-5", // K
      16: "bg-secondary", // DEF
      17: "bg-chart-5", // K
      23: "bg-muted", // FLEX
    };
    return colors[positionId] || "bg-muted";
  };

  const getPositionName = (positionId: number) => {
    const positions: Record<number, string> = {
      0: "QB",
      1: "QB",
      2: "RB", 
      3: "WR",
      4: "TE",
      5: "K",
      16: "DEF",
      17: "K",
      23: "FLEX",
    };
    return positions[positionId] || `POS_${positionId}`;
  };

  const filteredPlayers = currentData?.players?.filter((playerData: any) => {
    // Position filter
    if (selectedPosition !== "all") {
      const playerPosition = getPositionName(getPlayerPositionId(playerData));
      if (playerPosition !== selectedPosition) {
        return false;
      }
    }
    // Search filter
    if (searchTerm) {
      const name = getPlayerName(playerData);
      const teamId = getProTeamId(playerData) ? getProTeamId(playerData).toString() : '';
      if (!name.toLowerCase().includes(searchTerm.toLowerCase()) && !teamId.includes(searchTerm)) {
        return false;
      }
    }
    return true;
  }) || [];

  return (
    <>
      {/* Header Bar */}
      <header className="bg-card border-b border-border px-6 py-4">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-2xl font-bold text-foreground">Player Details</h2>
            <p className="text-muted-foreground">Browse player statistics and information</p>
          </div>
          <div className="flex items-center space-x-3">
            {viewMode === "waiver" && selectedLeagueId && (
              <Button
                variant="outline"
                onClick={() => {
                  const url = `/api/leagues/${selectedLeagueId}/waiver-wire/export?t=${Date.now()}`;
                  window.open(url, '_blank');
                }}
                data-testid="button-export-waiver"
              >
                <Download className="w-4 h-4 mr-2" />
                Export Waiver Wire
              </Button>
            )}
            {selectedLeagueId && (
              <Button
                variant="outline"
                onClick={() => {
                  const url = `/api/leagues/${selectedLeagueId}/roster-export?t=${Date.now()}`;
                  window.open(url, '_blank');
                }}
                data-testid="button-export-rosters"
              >
                <Download className="w-4 h-4 mr-2" />
                Export Team Rosters
              </Button>
            )}
            {/* Year dropdown removed. Season is set from league data. */}
            <Button
              variant="secondary"
              onClick={() => {
                if (viewMode === "waiver" && selectedLeagueId) {
                  queryClient.invalidateQueries({
                    queryKey: ["/api/leagues", selectedLeagueId, "waiver-wire"]
                  });
                } else {
                  queryClient.invalidateQueries({
                    queryKey: ["/api/players", selectedSport, selectedSeason]
                  });
                }
              }}
              data-testid="button-refresh"
            >
              <RefreshCw className="w-4 h-4 mr-2" />
              Refresh
            </Button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1 overflow-auto p-6">
        <Tabs value={viewMode} onValueChange={(value) => setViewMode(value as "all" | "waiver")} className="mb-6">
          <TabsList>
            <TabsTrigger value="all" data-testid="tab-all-players">All Players</TabsTrigger>
            <TabsTrigger value="waiver" data-testid="tab-waiver-wire">Waiver Wire</TabsTrigger>
          </TabsList>
        </Tabs>

        <Card data-testid="card-players">
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle className="flex items-center space-x-2">
                <UsersRound className="w-5 h-5" />
                <span>{viewMode === "waiver" ? "Waiver Wire Players" : "Player Database"}</span>
                {viewMode === "waiver" && waiverWireData && typeof waiverWireData === 'object' && 'total' in waiverWireData && (
                  <Badge variant="secondary" className="ml-2">
                    {(waiverWireData as any).total} available
                  </Badge>
                )}
              </CardTitle>
              <div className="flex items-center space-x-2">
                <Select
                  value={selectedPosition}
                  onValueChange={setSelectedPosition}
                  data-testid="select-position-filter"
                >
                  <SelectTrigger className="w-32">
                    <SelectValue placeholder="Position" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All Positions</SelectItem>
                    <SelectItem value="QB">QB</SelectItem>
                    <SelectItem value="RB">RB</SelectItem>
                    <SelectItem value="WR">WR</SelectItem>
                    <SelectItem value="TE">TE</SelectItem>
                    <SelectItem value="K">K</SelectItem>
                    <SelectItem value="DEF">DEF</SelectItem>
                  </SelectContent>
                </Select>
                <Search className="w-4 h-4 text-muted-foreground" />
                <Input
                  placeholder="Search players..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  className="w-64"
                  data-testid="input-search-players"
                />
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {viewMode === "waiver" && !selectedLeagueId ? (
              <div className="text-center py-8">
                <UsersRound className="w-12 h-12 text-muted-foreground mx-auto mb-4" />
                <p className="text-muted-foreground">No league loaded. Please load a league first.</p>
              </div>
            ) : currentLoading ? (
              <div className="space-y-3">
                {[...Array(10)].map((_, i) => (
                  <div key={i} className="animate-pulse flex space-x-4">
                    <div className="w-8 h-8 bg-muted rounded-full"></div>
                    <div className="flex-1 space-y-2">
                      <div className="h-4 bg-muted rounded w-3/4"></div>
                      <div className="h-3 bg-muted rounded w-1/2"></div>
                    </div>
                  </div>
                ))}
              </div>
            ) : filteredPlayers.length > 0 ? (
              <div className="rounded-md border">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Player</TableHead>
                      <TableHead>Position</TableHead>
                      <TableHead>Team</TableHead>
                      <TableHead>Opponent</TableHead>
                      <TableHead>Proj. Pts</TableHead>
                      <TableHead>% Owned</TableHead>
                      <TableHead>Status</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {filteredPlayers.slice(0, 100).map((player: any) => (
                      <TableRow
                        key={player.id}
                        className="hover:bg-muted/50"
                        data-testid={`row-player-${player.id}`}
                      >
                        <TableCell>
                          <div className="font-medium text-foreground">
                            {getPlayerName(player)}
                          </div>
                        </TableCell>
                        <TableCell>
                          <Badge 
                            className={`${getPositionColor(getPlayerPositionId(player))} text-white`}
                          >
                            {getPositionName(getPlayerPositionId(player))}
                          </Badge>
                        </TableCell>
                        <TableCell>
                          <span className="text-sm text-muted-foreground">
                            {getProTeamId(player) ? getTeamName(getProTeamId(player)) : "Free Agent"}
                          </span>
                        </TableCell>
                        <TableCell>
                          <span className="text-sm text-muted-foreground">
                            {getOpponent(player)}
                          </span>
                        </TableCell>
                        <TableCell>
                          <span className="text-sm text-foreground font-medium">
                            {getProjectedPoints(player)}
                          </span>
                        </TableCell>
                        <TableCell>
                          <span className="text-sm text-foreground">
                            {getOwnershipPercent(player)}%
                          </span>
                        </TableCell>
                        <TableCell>
                          <Badge variant={getInjuryStatus(player) === "Injured" ? "destructive" : "default"}>
                            {getInjuryStatus(player)}
                          </Badge>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            ) : (
              <div className="text-center py-8">
                <UsersRound className="w-12 h-12 text-muted-foreground mx-auto mb-4" />
                <p className="text-muted-foreground">
                  {searchTerm ? "No players found matching your search" : 
                   viewMode === "waiver" ? "No waiver wire players available" : "No player data available"}
                </p>
              </div>
            )}
          </CardContent>
        </Card>
      </main>
    </>
  );
}

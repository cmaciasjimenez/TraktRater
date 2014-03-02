﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TraktRater.UI;
using TraktRater.Extensions;
using TraktRater.TraktAPI;
using TraktRater.TraktAPI.DataStructures;
using TraktRater.Sites.API.IMDb;
using Microsoft.VisualBasic.FileIO;
using TraktRater.Settings;
using System.Net;
using System.Net.Cache;
using System.Text.RegularExpressions;
using System.Threading;

namespace TraktRater.Sites
{
    class IMDbWeb : IRateSite
    {
        bool ImportCancelled = false;
        private string Username;

        public IMDbWeb(string userName, bool enabled)
        {
            Enabled = !string.IsNullOrEmpty(userName) && enabled;
            this.Username = userName;
        }

        #region IRateSite

        public string Name
        {
            get { return "IMDbWeb"; }
        }

        public bool Enabled
        { get; set; }

        public void ImportRatings()
        {
            ImportCancelled = false;
            List<Dictionary<string, string>> watchedMovies = new List<Dictionary<string, string>>();
            List<Dictionary<string, string>> RateItems = new List<Dictionary<string, string>>();
            List<Dictionary<string, string>> watchlistItems = new List<Dictionary<string, string>>();

            #region Download Data
            UIUtils.UpdateStatus("Reading IMDb ratings from web...");

            WebClient client = new WebClient();
            client.Proxy = null; //TODO comment out
            int count = RateItems.Count;
            int MovieIndex = 1;
            do
            {
                count = RateItems.Count;
                string s = client.DownloadString("http://www.imdb.com/user/" + Username + "/ratings?start=" + MovieIndex + "&view=compact");
                int begin = 0;
                while ((begin = s.IndexOf("<tr data-item-id", begin)) > 0)
                {
                    var rateItem = new Dictionary<string, string>();
                    string sub = s.Substring(begin, s.IndexOf("</tr>", begin) - begin);


                    Regex reg = new Regex("<a href=\"/title/(tt[0-9]{5,15})/\">(.*)</a>");

                    var find = reg.Match(sub);
                    rateItem.Add("const", find.Groups[1].ToString());
                    rateItem.Add("Title", find.Groups[2].ToString());

                    reg = new Regex("<td class=\"your_ratings\">\\n    <a>([1-9][0-9]{0,1})</a>\\n</td>");
                    find = reg.Match(sub);
                    rateItem.Add("You rated", find.Groups[1].ToString());


                    reg = new Regex("<td class=\"year\">([1-2][0-9]{3})</td>");
                    find = reg.Match(sub);
                    rateItem.Add("Year", find.Groups[1].ToString());
                    reg = new Regex("<td class=\"title_type\"> (.*)</td>");
                    find = reg.Match(sub);
                    if (find.Groups[1].ToString() == string.Empty)
                        rateItem.Add("Title type", "Feature Film");
                    else
                        rateItem.Add("Title type", find.Groups[1].ToString());

                    RateItems.Add(rateItem);

                    begin += 10;
                }
                MovieIndex += 250;
            } while (count < RateItems.Count);

            //if watchlist sync is active
            if (AppSettings.IMDbSyncWatchlist)
            {
                UIUtils.UpdateStatus("Reading IMDb watchlist from web...");
                MovieIndex = 1;
                do
                {
                    count = watchlistItems.Count;
                    string s = client.DownloadString("http://www.imdb.com/user/" + Username + "/watchlist?start=" + MovieIndex + "&view=compact");
                    int begin = 0;
                    while ((begin = s.IndexOf("<tr data-item-id", begin)) > 0)
                    {
                        var rateItem = new Dictionary<string, string>();
                        string sub = s.Substring(begin, s.IndexOf("</tr>", begin) - begin);

                        Regex reg = new Regex("<a href=\"/title/(tt[0-9]{5,15})/\">(.*)</a>");

                        var find = reg.Match(sub);
                        rateItem.Add("const", find.Groups[1].ToString());
                        rateItem.Add("Title", find.Groups[2].ToString());

                        reg = new Regex("<td class=\"year\">([1-2][0-9]{3})</td>");
                        find = reg.Match(sub);
                        rateItem.Add("Year", find.Groups[1].ToString());
                        reg = new Regex("<td class=\"title_type\"> (.*)</td>");
                        find = reg.Match(sub);
                        if (find.Groups[1].ToString() == string.Empty)
                            rateItem.Add("Title type", "Feature Film");
                        else
                            rateItem.Add("Title type", find.Groups[1].ToString());

                        watchlistItems.Add(rateItem);

                        begin += 10;
                    }
                    MovieIndex += 250;
                } while (count < watchlistItems.Count);
            }
            #endregion

            if (ImportCancelled) return;

            // IMDb does not return the season and episode number for TV Episodes
            // so we should filter down to TV Shows and Movies only

            #region Rate and Mark as watched
            #region Movies
            var movies = RateItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Movie);
            if (movies.Count() > 0)
            {
                UIUtils.UpdateStatus(string.Format("Importing {0} movie ratings to trakt.tv.", movies.Count()));

                TraktRatingsResponse response = TraktAPI.TraktAPI.RateMovies(GetRateMoviesData(movies));
                if (response == null || response.Status != "success")
                {
                    UIUtils.UpdateStatus("Error importing movie ratings to trakt.tv.", true);
                    Thread.Sleep(2000);
                }

                // add to list of movies to mark as watched
                watchedMovies.AddRange(movies);
            }
            if (ImportCancelled) return;
            #endregion

            #region TV Shows
            var shows = RateItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Show);
            if (shows.Count() > 0)
            {
                UIUtils.UpdateStatus(string.Format("Importing {0} show ratings to trakt.tv.", shows.Count()));

                TraktRatingsResponse response = TraktAPI.TraktAPI.RateShows(GetRateShowsData(shows));
                if (response == null || response.Status != "success")
                {
                    UIUtils.UpdateStatus("Error importing show ratings to trakt.tv.", true);
                    Thread.Sleep(2000);
                }
            }
            if (ImportCancelled) return;
            #endregion

            #region Movies Mark as Watched

            if (AppSettings.MarkAsWatched && watchedMovies.Count > 0)
            {
                // mark all movies as watched if rated
                UIUtils.UpdateStatus(string.Format("Importing {0} IMDb Movies as Watched...", watchedMovies.Count));
                TraktMovieSyncResponse watchedMoviesResponse = TraktAPI.TraktAPI.SyncMovieLibrary(GetWatchedMoviesData(watchedMovies), TraktSyncModes.seen);
                if (watchedMoviesResponse == null || watchedMoviesResponse.Status != "success")
                {
                    UIUtils.UpdateStatus("Failed to send watched status for IMDb movies.", true);
                    Thread.Sleep(2000);
                    if (ImportCancelled) return;
                }
            }

            #endregion
            #endregion

            #region Sync Watchlist
            if (AppSettings.IMDbSyncWatchlist)
            {
                List<Dictionary<string, string>> watchlistMovies = new List<Dictionary<string, string>>();
                watchlistMovies.AddRange(watchlistItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Movie));
                if (watchlistMovies.Count() > 0)
                {
                    //add all movies to watchlist
                    UIUtils.UpdateStatus(string.Format("Importing {0} IMDb watchlist movies to trakt.tv ...", watchlistMovies.Count()));
                    TraktMovieSyncResponse watchlistMoviesResponse = TraktAPI.TraktAPI.SyncMovieLibrary(GetWatchedMoviesData(watchlistMovies), TraktSyncModes.watchlist);
                    if (watchlistMoviesResponse == null || watchlistMoviesResponse.Status != "success")
                    {
                        UIUtils.UpdateStatus("Failed to send watchlist for IMDb movies.", true);
                        Thread.Sleep(2000);
                        if (ImportCancelled) return;
                    }
                }

                List<Dictionary<string, string>> watchlistShows = new List<Dictionary<string, string>>();
                watchlistShows.AddRange(watchlistItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Show));
                if (watchlistShows.Count() > 0)
                {
                    //add all shows to watchlist
                    UIUtils.UpdateStatus(string.Format("Importing {0} IMDb watchlist shows to trakt.tv ...", watchlistShows.Count()));
                    TraktResponse watchlistMoviesResponse = TraktAPI.TraktAPI.SyncShowLibrary(GetWatchedShowsData(watchlistShows), TraktSyncModes.watchlist);
                    if (watchlistMoviesResponse == null || watchlistMoviesResponse.Status != "success")
                    {
                        UIUtils.UpdateStatus("Failed to send watchlist for IMDb shows.", true);
                        Thread.Sleep(2000);
                        if (ImportCancelled) return;
                    }
                }
            }
            #endregion
        }

        public void Cancel()
        {
            // signals to cancel import
            ImportCancelled = true;
        }
        #endregion

        private TraktMovieSync GetWatchedMoviesData(List<Dictionary<string, string>> movies)
        {
            var traktMovies = new List<TraktMovieSync.Movie>();

            traktMovies.AddRange(from movie in movies
                                 select new TraktMovieSync.Movie
                                 {
                                     IMDBID = movie[IMDbFieldMapping.cIMDbID],
                                     Title = movie[IMDbFieldMapping.cTitle],
                                     Year = int.Parse(movie[IMDbFieldMapping.cYear]).ToString()
                                 });

            var movieWatchedData = new TraktMovieSync
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                MovieList = traktMovies
            };

            return movieWatchedData;
        }

        private TraktRateMovies GetRateMoviesData(IEnumerable<Dictionary<string, string>> movies)
        {
            var traktMovies = new List<TraktMovie>();

            traktMovies.AddRange(from movie in movies
                                 select new TraktMovie
                                 {
                                     IMDbId = movie[IMDbFieldMapping.cIMDbID],
                                     Title = movie[IMDbFieldMapping.cTitle],
                                     Year = int.Parse(movie[IMDbFieldMapping.cYear]),
                                     Rating = int.Parse(movie[IMDbFieldMapping.cRating])
                                 });

            var movieRateData = new TraktRateMovies
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Movies = traktMovies
            };

            return movieRateData;
        }

        private TraktRateShows GetRateShowsData(IEnumerable<Dictionary<string, string>> shows)
        {
            var traktShows = new List<TraktShow>();

            traktShows.AddRange(from show in shows
                                select new TraktShow
                                {
                                    IMDbId = show[IMDbFieldMapping.cIMDbID],
                                    Title = show[IMDbFieldMapping.cTitle],
                                    Year = int.Parse(show[IMDbFieldMapping.cYear]),
                                    Rating = int.Parse(show[IMDbFieldMapping.cRating])
                                });

            var movieRateData = new TraktRateShows
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Shows = traktShows
            };

            return movieRateData;
        }

        private TraktShowSync GetWatchedShowsData(IEnumerable<Dictionary<string, string>> shows)
        {
            var traktShows = new List<TraktShowSync.Show>();

            traktShows.AddRange(from show in shows
                                select new TraktShowSync.Show
                                {
                                    IMDbId = show[IMDbFieldMapping.cIMDbID],
                                    Title = show[IMDbFieldMapping.cTitle],
                                    Year = show[IMDbFieldMapping.cYear]
                                });

            var traktShowsData = new TraktShowSync
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Showlist = traktShows
            };

            return traktShowsData;
        }
    }
}

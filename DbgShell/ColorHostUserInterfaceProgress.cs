/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/



using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Threading;
using MS.Dbg;


namespace MS.DbgShell
{
    internal partial class ColorHostUserInterface : PSHostUserInterface
    {
        /// <summary>
        /// 
        /// Called at the end of a prompt loop to take down any progress display that might have appeared and purge any 
        /// outstanding progress activity state.
        /// 
        /// </summary>

        internal
        void
        ResetProgress()
        {
            // destroy the data structures representing outstanding progress records
            // take down and destroy the progress display

            // [danthom] I don't know why PowerShell uses a timer here. Also, I don't know
            // how it actually works in regular PS--in my host, the timer tick (which runs
            // on a threadpool thread, because it's a System.Threading timer) races
            // against the pipeline execution thread, so depending on how the race goes,
            // you often end up with progress junk left on the screen.
            //
            // I could address this by enqueueing the timer tick stuff to the pipeline
            // thread (via parent.QueueActionToMainThreadDuringPipelineExecution), but
            // there would still be a problem with that--even the enqueue races against
            // the queue's completion, and there is no way to TryAdd to the queue (a
            // BlockingCollection) that doesn't throw if the collection has been
            // completed. So I'd have to add a
            // "Safe_TryQueueActionToMainThreadDuringPipelineExecution" method that takes
            // a lock. And since things seem to work fine without the timer stuff
            // anyway... let's just cut out the timer jazz.
            //
         // if (_progPaneUpdateTimer != null)
         // {
         //     // Stop update 'ProgressPane' and destroy timer
         //     _progPaneUpdateTimer.Dispose();
         //     _progPaneUpdateTimer = null;
         // }
            if (_progPane != null)
            {
                Util.Assert(_pendingProgress != null, "How can you have a progress pane and no backing data structure?");

                _progPane.Hide();
                _progPane = null;
            }
            _pendingProgress = null;
        }



        /// <summary>
        ///
        /// Invoked by ConsoleHostUserInterface.WriteProgress to update the set of outstanding activities for which 
        /// ProgressRecords have been received.
        ///
        /// </summary>

        private
        void
        HandleIncomingProgressRecord(Int64 sourceId, ProgressRecord record)
        {
            Util.Assert(record != null, "record should not be null");

            if (_pendingProgress == null)
            {
                Util.Assert(_progPane == null, "If there is no data struct, there shouldn't be a pane, either.");

                _pendingProgress = new PendingProgress();
            }

            _pendingProgress.Update(sourceId, record);

            if (_progPane == null)
            {
                // This is the first time we've received a progress record.
                // Create a progress pane,
                // then show it,
                // then create and start timer to update it.

                _progPane = new ProgressPane(this);

             // if (_progPaneUpdateTimer == null && _progPane != null)
             // {
             //     _progPane.Show(_pendingProgress);
             //     _progPaneUpdateTimer = new Timer( new TimerCallback(ProgressPaneUpdateTimerElapsed), null, UpdateTimerThreshold, Timeout.Infinite);
             // }
            }
        }



        /// <summary>
        ///
        /// TimerCallback for _progPaneUpdateTimer to update 'ProgressPane' and restart the timer.
        ///
        /// </summary>

     // private
     // void
     // ProgressPaneUpdateTimerElapsed(object sender)
     // {
     //     if (_progPane != null)
     //     {
     //         _progPane.Show(_pendingProgress);
     //     }
     //     if (_progPaneUpdateTimer != null)
     //     {
     //         _progPaneUpdateTimer.Change(UpdateTimerThreshold, Timeout.Infinite);
     //     }
     // }



        private
        void
        PreWrite()
        {
            if (_progPane != null)
            {
                _progPane.Hide();
            }
        }



        private
        void
        PostWrite()
        {
            if (_progPane != null)
            {
                _progPane.Show();
            }
        }



        private
        void
        PostWrite(ReadOnlySpan<char> value)
        {
            PostWrite();

          //if (_parent.IsTranscribing)
          //{
          //    try
          //    {
          //        _parent.WriteToTranscript(value);
          //    }
          //    catch (Exception e)
          //    {
          //        ConsoleHost.CheckForSevereException(e);
          //        _parent.IsTranscribing = false;
          //    }
          //}
        }



        private
        void
        PreRead()
        {
            if (_progPane != null)
            {
                _progPane.Hide();
            }
        }



        private
        void
        PostRead()
        {
            if (_progPane != null)
            {
                _progPane.Show();
            }
        }



        private
        void
        PostRead(string value)
        {
            PostRead();

          //if (_parent.IsTranscribing)
          //{
          //    try
          //    {
          //        // Reads always terminate with the enter key, so add that.
          //        _parent.WriteToTranscript(value + Crlf);
          //    }
          //    catch (Exception e)
          //    {
          //        ConsoleHost.CheckForSevereException(e);
          //        _parent.IsTranscribing = false;
          //    }
          //}
        }



        private ProgressPane _progPane = null;
        private PendingProgress _pendingProgress = null;
        // The timer update 'ProgressPane' every 'UpdateTimerThreshold' milliseconds
     // private Timer _progPaneUpdateTimer;
     // private const int UpdateTimerThreshold = 100;
    }
}   // namespace 




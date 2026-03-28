import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { mockSessions, mockUsers } from '@/features/mediscribe/mock-data';
import {
  buildSessionFromDraft,
  createPostVisitSummary,
  listSessionsMock,
} from '@/features/mediscribe/services/session-service';
import type {
  ClinicUser,
  ClinicalSession,
  MedicationRecommendationStatus,
  NewSessionPayload,
  SessionStatus,
} from '@/features/mediscribe/types';

interface SignInPayload {
  email: string;
  password: string;
}

interface MediScribeContextValue {
  currentUser: ClinicUser | null;
  sessions: ClinicalSession[];
  loading: boolean;
  authError: string | null;
  signIn: (payload: SignInPayload) => Promise<boolean>;
  signOut: () => void;
  createSessionDraft: (payload: NewSessionPayload) => Promise<string>;
  updateSessionStatus: (sessionId: string, status: SessionStatus) => void;
  updateTranscriptText: (sessionId: string, transcriptId: string, text: string) => void;
  updateSoapSection: (
    sessionId: string,
    section: 'subjective' | 'objective' | 'assessment' | 'plan',
    value: string,
  ) => void;
  updateRecommendationStatus: (
    sessionId: string,
    recommendationId: string,
    status: MedicationRecommendationStatus,
  ) => void;
  toggleDoctorValidation: (sessionId: string, recommendationId: string, validated: boolean) => void;
  updatePostVisitSummary: (sessionId: string, content: string) => void;
  regeneratePostVisitSummary: (sessionId: string) => void;
  approveAndSaveSession: (sessionId: string) => { ok: boolean; reason?: string };
}

const MediScribeContext = createContext<MediScribeContextValue | undefined>(undefined);

const PASSWORD = 'mediscribe123';

export function MediScribeProvider({ children }: { children: React.ReactNode }) {
  const [currentUser, setCurrentUser] = useState<ClinicUser | null>(null);
  const [sessions, setSessions] = useState<ClinicalSession[]>(mockSessions);
  const [loading, setLoading] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);

  const signIn = useCallback(async (payload: SignInPayload): Promise<boolean> => {
    setLoading(true);
    setAuthError(null);
    const allSessions = await listSessionsMock(mockSessions);
    setSessions(allSessions);

    const match = mockUsers.find((user) => user.email.toLowerCase() === payload.email.toLowerCase());
    if (!match || payload.password !== PASSWORD) {
      setLoading(false);
      setAuthError('Invalid credentials. Use demo doctor/staff accounts.');
      return false;
    }

    setCurrentUser(match);
    setLoading(false);
    return true;
  }, []);

  const signOut = useCallback(() => {
    setCurrentUser(null);
    setAuthError(null);
  }, []);

  const createSessionDraft = useCallback(
    async (payload: NewSessionPayload): Promise<string> => {
      if (!currentUser) {
        throw new Error('A signed-in user is required to create a session.');
      }

      const draft = buildSessionFromDraft(payload, currentUser.id, currentUser.name, currentUser.role);
      setSessions((prev) => [draft, ...prev]);
      return draft.id;
    },
    [currentUser],
  );

  const updateSessionStatus = useCallback((sessionId: string, status: SessionStatus) => {
    if (status === 'completed' && currentUser?.role !== 'doctor') {
      return;
    }

    setSessions((prev) =>
      prev.map((session) =>
        session.id === sessionId
          ? {
              ...session,
              status,
              auditEvents: [
                {
                  id: `audit-${Date.now()}`,
                  actorId: currentUser?.id ?? 'system',
                  action: `Status moved to ${status}`,
                  timestamp: new Date().toISOString(),
                },
                ...session.auditEvents,
              ],
            }
          : session,
      ),
    );
  }, [currentUser?.id, currentUser?.role]);

  const updateTranscriptText = useCallback((sessionId: string, transcriptId: string, text: string) => {
    setSessions((prev) =>
      prev.map((session) =>
        session.id === sessionId
          ? {
              ...session,
              transcript: session.transcript.map((entry) =>
                entry.id === transcriptId
                  ? {
                      ...entry,
                      text,
                    }
                  : entry,
              ),
            }
          : session,
      ),
    );
  }, []);

  const updateSoapSection = useCallback(
    (sessionId: string, section: 'subjective' | 'objective' | 'assessment' | 'plan', value: string) => {
      setSessions((prev) =>
        prev.map((session) =>
          session.id === sessionId
            ? {
                ...session,
                status: session.status === 'pending' ? 'in_review' : session.status,
                soapNote: {
                  ...session.soapNote,
                  [section]: value,
                },
              }
            : session,
        ),
      );
    },
    [],
  );

  const updateRecommendationStatus = useCallback(
    (sessionId: string, recommendationId: string, status: MedicationRecommendationStatus) => {
      setSessions((prev) =>
        prev.map((session) =>
          session.id === sessionId
            ? {
                ...session,
                medicationRecommendations: session.medicationRecommendations.map((recommendation) =>
                  recommendation.id === recommendationId
                    ? {
                        ...recommendation,
                        status,
                      }
                    : recommendation,
                ),
              }
            : session,
        ),
      );
    },
    [],
  );

  const toggleDoctorValidation = useCallback(
    (sessionId: string, recommendationId: string, validated: boolean) => {
      setSessions((prev) =>
        prev.map((session) =>
          session.id === sessionId
            ? {
                ...session,
                medicationRecommendations: session.medicationRecommendations.map((recommendation) =>
                  recommendation.id === recommendationId
                    ? {
                        ...recommendation,
                        validatedByDoctor: validated,
                        status: validated ? 'approved' : 'reviewed',
                      }
                    : recommendation,
                ),
              }
            : session,
        ),
      );
    },
    [],
  );

  const updatePostVisitSummary = useCallback((sessionId: string, content: string) => {
    setSessions((prev) =>
      prev.map((session) =>
        session.id === sessionId
          ? {
              ...session,
              postVisitSummary: session.postVisitSummary
                ? {
                    ...session.postVisitSummary,
                    content,
                    editedAt: new Date().toISOString(),
                  }
                : {
                    id: `summary-${Date.now()}`,
                    generatedAt: new Date().toISOString(),
                    content,
                  },
            }
          : session,
      ),
    );
  }, []);

  const regeneratePostVisitSummary = useCallback((sessionId: string) => {
    setSessions((prev) =>
      prev.map((session) =>
        session.id === sessionId
          ? {
              ...session,
              postVisitSummary: createPostVisitSummary(session),
            }
          : session,
      ),
    );
  }, []);

  const approveAndSaveSession = useCallback(
    (sessionId: string): { ok: boolean; reason?: string } => {
      if (!currentUser || currentUser.role !== 'doctor') {
        return { ok: false, reason: 'Only doctors can approve and save sessions.' };
      }

      const session = sessions.find((item) => item.id === sessionId);
      if (!session) {
        return { ok: false, reason: 'Session not found.' };
      }

      const allValidated = session.medicationRecommendations.every(
        (recommendation) => !recommendation.requiresDoctorValidation || recommendation.validatedByDoctor,
      );

      if (!allValidated) {
        return {
          ok: false,
          reason: 'Validate all medication recommendations before final approval.',
        };
      }

      setSessions((prev) =>
        prev.map((item) =>
          item.id === sessionId
            ? {
                ...item,
                status: 'completed',
                soapNote: {
                  ...item.soapNote,
                  approvedAt: new Date().toISOString(),
                  approvedBy: currentUser.name,
                },
                postVisitSummary: item.postVisitSummary ?? createPostVisitSummary(item),
                auditEvents: [
                  {
                    id: `audit-${Date.now()}`,
                    actorId: currentUser.id,
                    action: 'Doctor approved and saved session',
                    timestamp: new Date().toISOString(),
                  },
                  ...item.auditEvents,
                ],
              }
            : item,
        ),
      );

      return { ok: true };
    },
    [currentUser, sessions],
  );

  const value = useMemo<MediScribeContextValue>(
    () => ({
      currentUser,
      sessions,
      loading,
      authError,
      signIn,
      signOut,
      createSessionDraft,
      updateSessionStatus,
      updateTranscriptText,
      updateSoapSection,
      updateRecommendationStatus,
      toggleDoctorValidation,
      updatePostVisitSummary,
      regeneratePostVisitSummary,
      approveAndSaveSession,
    }),
    [
      approveAndSaveSession,
      authError,
      createSessionDraft,
      currentUser,
      loading,
      regeneratePostVisitSummary,
      sessions,
      signIn,
      signOut,
      toggleDoctorValidation,
      updatePostVisitSummary,
      updateRecommendationStatus,
      updateSessionStatus,
      updateSoapSection,
      updateTranscriptText,
    ],
  );

  return <MediScribeContext.Provider value={value}>{children}</MediScribeContext.Provider>;
}

export function useMediScribe(): MediScribeContextValue {
  const context = useContext(MediScribeContext);
  if (!context) {
    throw new Error('useMediScribe must be used inside MediScribeProvider');
  }
  return context;
}

export const demoCredentials = {
  doctorEmail: 'doctor@mediscribe.app',
  staffEmail: 'staff@mediscribe.app',
  password: PASSWORD,
};

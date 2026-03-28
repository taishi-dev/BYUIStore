import { API_CONFIG, mockApiDelay } from '@/lib/api';
import type {
  ClinicalSession,
  NewSessionPayload,
  SoapNote,
  TranscriptEntry,
  SessionStatus,
  PostVisitSummary,
  MedicationRecommendation,
} from '@/features/mediscribe/types';

const createId = (prefix: string): string => `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1000)}`;

export async function listSessionsMock(sessions: ClinicalSession[]): Promise<ClinicalSession[]> {
  void API_CONFIG.BASE_URL;
  await mockApiDelay();
  return sessions;
}

export function buildSessionFromDraft(
  payload: NewSessionPayload,
  ownerId: string,
  ownerName: string,
  ownerRole: 'doctor' | 'staff',
): ClinicalSession {
  const startedAt = new Date().toISOString();

  const transcript: TranscriptEntry[] = [
    {
      id: createId('transcript-doctor'),
      speaker: 'doctor',
      text: payload.transcriptDoctorText,
      timestamp: startedAt,
      confidence: 0.91,
    },
    {
      id: createId('transcript-patient'),
      speaker: 'patient',
      text: payload.transcriptPatientText,
      timestamp: startedAt,
      confidence: 0.86,
    },
  ];

  const soapNote: SoapNote = {
    subjective:
      payload.transcriptPatientText ||
      'Patient-reported symptoms captured from consultation transcript.',
    objective:
      'Vitals and objective findings to be validated by clinician review.',
    assessment: 'Provisional assessment draft generated for doctor review.',
    plan: 'Initial treatment plan draft. Finalize after medication validation.',
  };

  const medicationRecommendations: MedicationRecommendation[] = [
    {
      id: createId('med'),
      name: 'Draft recommendation pending guideline source',
      dosage: 'To be validated',
      frequency: 'To be validated',
      ageGroup: payload.age < 18 ? 'Pediatric' : 'Adult',
      status: 'suggested',
      safetyNotes: 'Recommendation only. Requires doctor validation before use.',
      rationale: 'Generated from session context and patient profile draft.',
      requiresDoctorValidation: true,
      validatedByDoctor: false,
    },
  ];

  return {
    id: createId('session'),
    patient: {
      id: createId('patient'),
      fullName: payload.patientName,
      age: payload.age,
      sex: payload.sex,
      allergies: payload.allergies,
      ineffectiveMedications: payload.ineffectiveMedications,
    },
    ownerId,
    ownerName,
    ownerRole,
    startedAt,
    status: 'pending',
    transcript,
    soapNote,
    medicationRecommendations,
    auditEvents: [
      {
        id: createId('audit'),
        actorId: ownerId,
        action: 'Session created from new consultation draft',
        timestamp: startedAt,
      },
    ],
  };
}

export function createPostVisitSummary(session: ClinicalSession): PostVisitSummary {
  const generatedAt = new Date().toISOString();

  return {
    id: createId('summary'),
    generatedAt,
    content:
      `Visit Summary for ${session.patient.fullName}: ` +
      `${session.soapNote.assessment} Plan: ${session.soapNote.plan} ` +
      `Validated medications: ${session.medicationRecommendations
        .filter((medication) => medication.validatedByDoctor)
        .map((medication) => `${medication.name} (${medication.dosage}, ${medication.frequency})`)
        .join('; ') || 'No validated medications yet.'}`,
  };
}

export function nextWorkflowStatus(current: SessionStatus): SessionStatus {
  if (current === 'pending') {
    return 'in_review';
  }
  if (current === 'in_review') {
    return 'completed';
  }
  return current;
}

import type { ClinicUser, ClinicalSession } from '@/features/mediscribe/types';

const now = new Date();

const isoAt = (hoursAgo: number): string => {
  const value = new Date(now.getTime() - hoursAgo * 60 * 60 * 1000);
  return value.toISOString();
};

export const mockUsers: ClinicUser[] = [
  {
    id: 'user-doc-1',
    name: 'Dr. Vivian Lin',
    email: 'doctor@mediscribe.app',
    role: 'doctor',
  },
  {
    id: 'user-staff-1',
    name: 'Morgan Hale',
    email: 'staff@mediscribe.app',
    role: 'staff',
  },
];

export const mockSessions: ClinicalSession[] = [
  {
    id: 'sess-001',
    patient: {
      id: 'pat-001',
      fullName: 'Elena Park',
      age: 34,
      sex: 'female',
      allergies: ['Penicillin'],
      ineffectiveMedications: ['Cetirizine'],
    },
    ownerId: 'user-staff-1',
    ownerName: 'Morgan Hale',
    ownerRole: 'staff',
    startedAt: isoAt(1.8),
    status: 'in_review',
    transcript: [
      {
        id: 't-001-d',
        speaker: 'doctor',
        text: 'Tell me more about your nasal congestion and how long this has been present.',
        timestamp: isoAt(1.75),
        confidence: 0.94,
      },
      {
        id: 't-001-p',
        speaker: 'patient',
        text: 'It has been about 5 days, mostly at night, with mild facial pressure.',
        timestamp: isoAt(1.74),
        confidence: 0.89,
      },
    ],
    soapNote: {
      subjective: '5-day history of congestion, nighttime worsening, mild facial pressure. No dyspnea. No fever reported.',
      objective: 'Alert, speaking full sentences. Mild tenderness over maxillary area noted in exam summary.',
      assessment: 'Likely acute rhinosinusitis, uncomplicated.',
      plan: 'Supportive treatment, hydration, saline rinses. Consider intranasal steroid and reassess if worsening.',
    },
    medicationRecommendations: [
      {
        id: 'med-001',
        name: 'Fluticasone nasal spray',
        dosage: '50 mcg per spray',
        frequency: '2 sprays each nostril once daily',
        ageGroup: 'Adult',
        status: 'reviewed',
        safetyNotes: 'Avoid if severe untreated nasal infection. Monitor for epistaxis.',
        rationale: 'Improves inflammatory nasal symptoms and nighttime congestion.',
        requiresDoctorValidation: true,
        validatedByDoctor: false,
      },
    ],
    auditEvents: [
      {
        id: 'audit-001',
        actorId: 'user-staff-1',
        action: 'Session created',
        timestamp: isoAt(1.8),
      },
      {
        id: 'audit-002',
        actorId: 'user-staff-1',
        action: 'SOAP draft refined',
        timestamp: isoAt(1.45),
      },
    ],
  },
  {
    id: 'sess-002',
    patient: {
      id: 'pat-002',
      fullName: 'Carter Wu',
      age: 11,
      sex: 'male',
      allergies: ['Ibuprofen'],
      ineffectiveMedications: ['Loratadine'],
    },
    ownerId: 'user-doc-1',
    ownerName: 'Dr. Vivian Lin',
    ownerRole: 'doctor',
    startedAt: isoAt(3.25),
    status: 'pending',
    transcript: [
      {
        id: 't-002-d',
        speaker: 'doctor',
        text: 'When did the sore throat start, and are there any swallowing difficulties?',
        timestamp: isoAt(3.2),
        confidence: 0.88,
      },
      {
        id: 't-002-p',
        speaker: 'patient',
        text: 'Started yesterday morning; it hurts but I can still swallow liquids.',
        timestamp: isoAt(3.18),
        confidence: 0.9,
      },
    ],
    soapNote: {
      subjective: 'Pediatric patient with 1-day sore throat and painful swallowing. No airway symptoms.',
      objective: 'Mild pharyngeal erythema. Afebrile in clinic intake vitals.',
      assessment: 'Acute pharyngitis, likely viral etiology pending rapid test protocol.',
      plan: 'Symptom control, hydration, return precautions. Monitor fever and oral intake.',
    },
    medicationRecommendations: [
      {
        id: 'med-002',
        name: 'Acetaminophen',
        dosage: '15 mg/kg per dose',
        frequency: 'Every 6 hours as needed (max daily dose per guideline)',
        ageGroup: 'Pediatric',
        status: 'suggested',
        safetyNotes: 'Verify weight-based maximum daily dose. Avoid duplicate acetaminophen products.',
        rationale: 'First-line analgesic/antipyretic when NSAID allergy exists.',
        requiresDoctorValidation: true,
        validatedByDoctor: false,
      },
    ],
    auditEvents: [
      {
        id: 'audit-003',
        actorId: 'user-doc-1',
        action: 'Session created',
        timestamp: isoAt(3.25),
      },
    ],
  },
  {
    id: 'sess-003',
    patient: {
      id: 'pat-003',
      fullName: 'Mila Gomez',
      age: 46,
      sex: 'female',
      allergies: ['None reported'],
      ineffectiveMedications: ['Omeprazole'],
    },
    ownerId: 'user-doc-1',
    ownerName: 'Dr. Vivian Lin',
    ownerRole: 'doctor',
    startedAt: isoAt(5.5),
    status: 'completed',
    transcript: [
      {
        id: 't-003-d',
        speaker: 'doctor',
        text: 'You mentioned reflux symptoms despite prior therapy. Any nighttime awakening?',
        timestamp: isoAt(5.45),
        confidence: 0.95,
      },
      {
        id: 't-003-p',
        speaker: 'patient',
        text: 'Yes, about three nights this week with burning sensation.',
        timestamp: isoAt(5.44),
        confidence: 0.92,
      },
    ],
    soapNote: {
      subjective: 'Persistent reflux symptoms with nocturnal episodes despite previous PPI trial.',
      objective: 'No acute distress. Abdomen benign in recorded exam summary.',
      assessment: 'GERD with partial response to prior medication.',
      plan: 'Adjust therapy, meal timing counseling, follow-up in 4 weeks. Consider GI referral if persistent.',
      approvedAt: isoAt(4.9),
      approvedBy: 'Dr. Vivian Lin',
    },
    medicationRecommendations: [
      {
        id: 'med-003',
        name: 'Famotidine',
        dosage: '20 mg',
        frequency: 'Twice daily',
        ageGroup: 'Adult',
        status: 'approved',
        safetyNotes: 'Adjust dose if renal impairment is present.',
        rationale: 'Alternative acid suppression strategy after incomplete PPI response.',
        requiresDoctorValidation: true,
        validatedByDoctor: true,
      },
    ],
    postVisitSummary: {
      id: 'sum-003',
      generatedAt: isoAt(4.9),
      editedAt: isoAt(4.8),
      content:
        'Visit Summary: Persistent reflux symptoms reviewed. Medication plan adjusted and lifestyle guidance reinforced. Follow-up arranged in 4 weeks with escalation plan for ongoing symptoms.',
    },
    auditEvents: [
      {
        id: 'audit-004',
        actorId: 'user-doc-1',
        action: 'Doctor approved SOAP and medication plan',
        timestamp: isoAt(4.9),
      },
    ],
  },
];
